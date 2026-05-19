using System.Security.Claims;
using System.Text.Json;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;
using it15_webproject_mvc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static it15_webproject_mvc.Constants.StatusConstants;

namespace it15_webproject_mvc.Controllers
{
    [Authorize(Roles = "Staff")]
    public class StaffController : BaseController
    {
        private const string EntityStagingRecord = "StagingRecord";
        private readonly ApplicationDbContext _context;
        private readonly ApiIntegrationService _apiService;
        private readonly IAuditService _audit;
        private readonly INotificationService _notif;
        private readonly SubscriptionService _subscriptionService;

        public StaffController(
            ApplicationDbContext context,
            ApiIntegrationService apiService,
            IAuditService audit,
            INotificationService notif,
            SubscriptionService subscriptionService)
        {
            _context = context;
            _apiService = apiService;
            _audit = audit;
            _notif = notif;
            _subscriptionService = subscriptionService;
        }

        public async Task<IActionResult> StaffNav(string section = "dashboard", string? viewBatchId = null, int page = 1)
        {
            await SetSectionAndOrganizationAsync(_context, _subscriptionService, section);

            switch (section.ToLower())
            {
                case "dashboard":
                    await LoadDashboardData();
                    break;
                case "upload":
                    await LoadUploadData();
                    break;
                case "verify":
                    await LoadVerifyData(viewBatchId, page);
                    break;
                case "submit":
                    await LoadSubmitData();
                    break;
                case "reports":
                    await LoadReportsData();
                    break;
                case "sources":
                    await LoadSourcesData();
                    break;
                case "history":
                    await LoadHistoryData();
                    break;
            }

            return View();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AddSource(string sourceName, string apiBaseUrl, string apiEndpoint, string apiKey, string authMethod, string targetTable)
        {
            if (string.IsNullOrWhiteSpace(sourceName) ||
                string.IsNullOrWhiteSpace(apiBaseUrl) ||
                string.IsNullOrWhiteSpace(apiEndpoint) ||
                string.IsNullOrWhiteSpace(targetTable))
            {
                TempData["SourceError"] = "Please complete all required fields before adding a source.";
                return RedirectToAction("StaffNav", new { section = "sources" });
            }

            var userId = GetCurrentUserId();
            var orgId = GetCurrentOrgId();
            var org = await _context.Organizations.FindAsync(orgId);
            var subPlan = org?.SubscriptionPlan ?? "Free";
            var currentSourceCount = await _context.DataSources.CountAsync(s => s.OrganizationID == orgId);
            var maxSources = subPlan switch { "Premium" => int.MaxValue, "Basic" => 3, _ => 1 };
            if (currentSourceCount >= maxSources)
            {
                TempData["SourceError"] = $"Source limit reached for your {subPlan} plan ({maxSources} source(s) max). Ask your admin to upgrade.";
                return RedirectToAction("StaffNav", new { section = "sources" });
            }

            var source = new DataSource
            {
                SourceName = sourceName,
                ApiBaseUrl = apiBaseUrl,
                ApiEndpoint = apiEndpoint,
                ApiKey = apiKey ?? "",
                AuthMethod = authMethod,
                TargetTable = targetTable,
                Status = StatusActive,
                CreatedByUserID = userId,
                OrganizationID = orgId,
                Created_at = DateTime.UtcNow
            };

            _context.DataSources.Add(source);
            await _context.SaveChangesAsync();

            _audit.Log("Source Added", "DataSource", source.DataSourceID, source.SourceName,
                $"API: {source.ApiBaseUrl}{source.ApiEndpoint} | Auth: {source.AuthMethod} | Target: {source.TargetTable}",
                userId, orgId);
            await _context.SaveChangesAsync(); 

            return RedirectToAction("StaffNav", new { section = "sources" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestConnection(int dataSourceId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var source = await _context.DataSources.FirstOrDefaultAsync(s => s.DataSourceID == dataSourceId && s.OrganizationID == orgId);
            if (source == null) return NotFound();

            var result = await _apiService.TestConnectionAsync(source.ApiBaseUrl, source.ApiEndpoint, source.ApiKey, source.AuthMethod);

            TempData["TestResult"] = result.Success ? "success" : "fail";
            TempData["TestMessage"] = result.Success ? $"Connection successful! (HTTP {result.StatusCode})" : result.ErrorMessage;
            TempData["TestSourceId"] = dataSourceId;

            _audit.Log("Connection Tested", "DataSource", source.DataSourceID, source.SourceName,
                result.Success ? $"Success (HTTP {result.StatusCode})" : $"Failed: {result.ErrorMessage}",
                GetCurrentUserId(), orgId);
            await _context.SaveChangesAsync();

            return RedirectToAction("StaffNav", new { section = "sources" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSource(int dataSourceId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var source = await _context.DataSources.FirstOrDefaultAsync(s => s.DataSourceID == dataSourceId && s.OrganizationID == orgId);
            if (source == null) return NotFound();

            var sourceName = source.SourceName;
            _context.DataSources.Remove(source);

            _audit.Log("Source Deleted", "DataSource", dataSourceId, sourceName,
                $"Removed API source '{sourceName}'",
                GetCurrentUserId(), orgId);
            await _context.SaveChangesAsync();

            return RedirectToAction("StaffNav", new { section = "sources" });
        }

        // === STEP 1: PULL DATA FROM API ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PullFromApi(int dataSourceId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var userId = GetCurrentUserId();
            var orgId = GetCurrentOrgId();
            var source = await _context.DataSources.FirstOrDefaultAsync(s => s.DataSourceID == dataSourceId && s.OrganizationID == orgId);
            if (source == null) return NotFound();

            // Guard: prevent duplicate pulls if this source already has unsubmitted staging records
            var existingBatch = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => r.DataSourceID == dataSourceId && r.SubmissionID == null)
                .Select(r => r.BatchId)
                .FirstOrDefaultAsync();

            if (existingBatch != null)
            {
                TempData["PullError"] = $"This source already has an unsubmitted batch ({existingBatch}). Please verify and submit it first, or discard it before pulling again.";
                return RedirectToAction("StaffNav", new { section = "upload" });
            }

            var result = await _apiService.PullDataAsync(source.ApiBaseUrl, source.ApiEndpoint, source.ApiKey, source.AuthMethod);

            if (!result.Success)
            {
                TempData["PullError"] = result.ErrorMessage;
                return RedirectToAction("StaffNav", new { section = "upload" });
            }

            // Generate a batch ID for this pull
            var batchId = $"BATCH-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{source.DataSourceID}";

            // Store each row as a staging record
            var rowNumber = 1;
            foreach (var row in result.Rows)
            {
                var record = new StagingRecord
                {
                    DataSourceID = source.DataSourceID,
                    BatchId = batchId,
                    RawData = JsonSerializer.Serialize(row),
                    RowNumber = rowNumber++,
                    ValidationStatus = StatusPending,
                    Pulled_at = DateTime.UtcNow,
                    PulledByUserID = userId
                };
                _context.StagingRecords.Add(record);
            }

            // Update last sync
            source.Last_sync = DateTime.UtcNow;

            _audit.Log("Data Pulled", EntityStagingRecord, source.DataSourceID, source.SourceName,
                $"Batch: {batchId} | Rows: {result.RowCount} | Target: {source.TargetTable}",
                userId, orgId);

            await _context.SaveChangesAsync(); // single save for staging records + source sync + audit log

            TempData["PullSuccess"] = $"Successfully pulled {result.RowCount} rows from {source.SourceName}";
            TempData["PullBatchId"] = batchId;

            return RedirectToAction("StaffNav", new { section = "verify" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRecordMessage(int recordId, string? message)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var userId = GetCurrentUserId();

            var record = await _context.StagingRecords
                .Include(r => r.DataSource)
                .FirstOrDefaultAsync(r => r.StagingRecordID == recordId && r.DataSource!.OrganizationID == orgId);

            if (record == null) return NotFound();

            record.ValidationMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

            _audit.Log("Record Comment Updated", EntityStagingRecord, record.StagingRecordID, record.BatchId,
                $"Row #{record.RowNumber} message updated",
                userId, orgId);

            await _context.SaveChangesAsync();

            TempData["ValidateSuccess"] = $"Comment updated for row #{record.RowNumber}.";
            TempData["ValidateBatchId"] = record.BatchId;

            return RedirectToAction("StaffNav", new { section = "verify", viewBatchId = record.BatchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRecordValid(int recordId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var userId = GetCurrentUserId();

            var record = await _context.StagingRecords
                .Include(r => r.DataSource)
                .FirstOrDefaultAsync(r => r.StagingRecordID == recordId && r.DataSource!.OrganizationID == orgId);

            if (record == null) return NotFound();

            record.ValidationStatus = StatusValid;

            _audit.Log("Record Marked Valid", EntityStagingRecord, record.StagingRecordID, record.BatchId,
                $"Row #{record.RowNumber} marked valid",
                userId, orgId);

            await _context.SaveChangesAsync();

            TempData["ValidateSuccess"] = $"Row #{record.RowNumber} marked valid.";
            TempData["ValidateBatchId"] = record.BatchId;

            return RedirectToAction("StaffNav", new { section = "verify", viewBatchId = record.BatchId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRecordInvalid(int recordId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var userId = GetCurrentUserId();

            var record = await _context.StagingRecords
                .Include(r => r.DataSource)
                .FirstOrDefaultAsync(r => r.StagingRecordID == recordId && r.DataSource!.OrganizationID == orgId);

            if (record == null) return NotFound();

            record.ValidationStatus = StatusError;

            _audit.Log("Record Marked Invalid", EntityStagingRecord, record.StagingRecordID, record.BatchId,
                $"Row #{record.RowNumber} marked invalid",
                userId, orgId);

            await _context.SaveChangesAsync();

            TempData["ValidateSuccess"] = $"Row #{record.RowNumber} marked invalid.";
            TempData["ValidateBatchId"] = record.BatchId;

            return RedirectToAction("StaffNav", new { section = "verify", viewBatchId = record.BatchId });
        }

        // === STEP 2: VERIFY / VALIDATE DATA ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidateBatch(string batchId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var records = await _context.StagingRecords
                .Where(r => r.BatchId == batchId && r.DataSource!.OrganizationID == orgId)
                .ToListAsync();

            foreach (var record in records)
            {
                var row = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(record.RawData);
                var issues = new List<string>();

                if (row != null)
                {
                    foreach (var kvp in row)
                    {
                        // Check for null/empty values
                        if (kvp.Value.ValueKind == JsonValueKind.Null ||
                            (kvp.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(kvp.Value.GetString())))
                        {
                            issues.Add($"Column '{kvp.Key}' is empty or null");
                        }
                    }
                }

                if (issues.Count > 0)
                {
                    record.ValidationStatus = issues.Any(i => i.Contains("is empty")) ? StatusWarning : StatusError;
                    record.ValidationMessage = string.Join("; ", issues);
                }
                else
                {
                    record.ValidationStatus = StatusValid;
                    record.ValidationMessage = null;
                }
            }

            var validCount = records.Count(r => r.ValidationStatus == StatusValid);
            var warnCount = records.Count(r => r.ValidationStatus == StatusWarning);
            var errCount = records.Count(r => r.ValidationStatus == StatusError);
            _audit.Log("Batch Validated", EntityStagingRecord, null, batchId,
                $"Rows: {records.Count} | Valid: {validCount} | Warnings: {warnCount} | Errors: {errCount}",
                GetCurrentUserId(), orgId);

            await _context.SaveChangesAsync(); // single save for validation updates + audit log

            TempData["ValidateSuccess"] = $"Validated {records.Count} rows in batch {batchId}";
            TempData["ValidateBatchId"] = batchId;

            return RedirectToAction("StaffNav", new { section = "verify" });
        }

        // === CORRECT FLAGGED RECORD ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorrectRecord(int recordId, string correctedJson)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var userId = GetCurrentUserId();

            var record = await _context.StagingRecords
                .Include(r => r.DataSource)
                .FirstOrDefaultAsync(r => r.StagingRecordID == recordId && r.DataSource!.OrganizationID == orgId);

            if (record == null) return NotFound();

            // Validate that the corrected JSON is parseable
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(correctedJson);
                if (parsed == null || parsed.Count == 0)
                {
                    TempData["CorrectError"] = "Invalid JSON data provided.";
                    return RedirectToAction("StaffNav", new { section = "verify" });
                }
            }
            catch
            {
                TempData["CorrectError"] = "Could not parse the corrected data. Please provide valid JSON.";
                return RedirectToAction("StaffNav", new { section = "verify" });
            }

            var oldStatus = record.ValidationStatus;
            record.RawData = correctedJson;
            record.ValidationStatus = StatusPending;
            record.ValidationMessage = null;

            _audit.Log("Record Corrected", EntityStagingRecord, record.StagingRecordID, record.BatchId,
                $"Row #{record.RowNumber} corrected | Previous status: {oldStatus}",
                userId, orgId);

            await _context.SaveChangesAsync();

            TempData["ValidateSuccess"] = $"Record #{record.RowNumber} has been corrected. Re-validate the batch to update its status.";
            TempData["ValidateBatchId"] = record.BatchId;

            return RedirectToAction("StaffNav", new { section = "verify" });
        }

        // === STEP 3: SUBMIT FOR INTEGRATION ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitBatch(string batchId, string loadMode, string? notes)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var userId = GetCurrentUserId();
            var orgId = GetCurrentOrgId();

            var records = await _context.StagingRecords
                .Include(r => r.DataSource)
                .Where(r => r.BatchId == batchId && r.DataSource!.OrganizationID == orgId)
                .ToListAsync();

            if (!records.Any()) return NotFound();

            var firstRecord = records[0];
            var validCount = records.Count(r => r.ValidationStatus == StatusValid);
            var errorCount = records.Count(r => r.ValidationStatus == StatusError);

            var submission = new DataSubmission
            {
                BatchId = batchId,
                DataSourceID = firstRecord.DataSourceID,
                TargetTable = firstRecord.DataSource?.TargetTable ?? StatusUnknown,
                TotalRows = records.Count,
                ValidRows = validCount,
                ErrorRows = errorCount,
                LoadMode = loadMode ?? "Append",
                Status = StatusSubmitted,
                Notes = notes,
                Created_at = DateTime.UtcNow,
                Submitted_at = DateTime.UtcNow,
                SubmittedByUserID = userId,
                OrganizationID = orgId
            };

            _context.DataSubmissions.Add(submission);
            await _context.SaveChangesAsync(); // save to generate SubmissionID

            // Link staging records to submission
            foreach (var record in records)
            {
                record.SubmissionID = submission.SubmissionID;
            }

            _audit.Log("Batch Submitted", "DataSubmission", submission.SubmissionID, batchId,
                $"Target: {submission.TargetTable} | Mode: {submission.LoadMode} | Valid: {validCount}/{records.Count} rows",
                userId, orgId);

            await _context.SaveChangesAsync(); // single save for record links + audit log

            // Notify all Managers in the organization
            await _notif.NotifyRoleAsync(orgId, "Manager",
                "New Submission Pending Approval",
                $"Batch {batchId} with {validCount} valid rows submitted for integration.",
                "Info",
                "/Manager/ManagerNav?section=approvals");

            TempData["SubmitSuccess"] = $"Batch {batchId} submitted for integration ({validCount} valid rows)";

            return RedirectToAction("StaffNav", new { section = "submit" });
        }

        // === DISCARD UNSUBMITTED BATCH ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DiscardBatch(int dataSourceId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var userId = GetCurrentUserId();
            var orgId = GetCurrentOrgId();
            var source = await _context.DataSources.FirstOrDefaultAsync(s => s.DataSourceID == dataSourceId && s.OrganizationID == orgId);
            if (source == null) return NotFound();

            var orphanedRecords = await _context.StagingRecords
                .Where(r => r.DataSourceID == dataSourceId && r.SubmissionID == null)
                .ToListAsync();

            if (!orphanedRecords.Any())
            {
                TempData["PullError"] = "No unsubmitted batch found for this source.";
                return RedirectToAction("StaffNav", new { section = "upload" });
            }

            var batchId = orphanedRecords[0].BatchId;
            var count = orphanedRecords.Count;
            _context.StagingRecords.RemoveRange(orphanedRecords);

            _audit.Log("Batch Discarded", EntityStagingRecord, source.DataSourceID, source.SourceName,
                $"Discarded batch {batchId} | {count} staging records removed",
                userId, orgId);

            await _context.SaveChangesAsync();

            TempData["PullSuccess"] = $"Discarded {count} staging records from batch {batchId}. You can now pull fresh data from '{source.SourceName}'.";
            return RedirectToAction("StaffNav", new { section = "upload" });
        }

        // === PARTIAL VIEWS ===

        public IActionResult StaffIndex() => View();
        public IActionResult StaffContent() => View();
        public IActionResult StaffApiSources() => View();
        public IActionResult StaffReports() => View();

        // === PRIVATE HELPERS ===

        private async Task LoadDashboardData()
        {
            var orgId = GetCurrentOrgId();

            var orgSourceIds = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .Select(s => s.DataSourceID)
                .ToListAsync();

            var sources = orgSourceIds.Count;
            var totalPulled = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => orgSourceIds.Contains(r.DataSourceID))
                .Select(r => r.BatchId).Distinct().CountAsync();
            var pendingVerify = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => r.ValidationStatus == StatusPending && orgSourceIds.Contains(r.DataSourceID))
                .Select(r => r.BatchId).Distinct().CountAsync();

            // Consolidate submission counts into a single query
            var submissionCounts = await _context.DataSubmissions
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var submitted = submissionCounts.Sum(s => s.Count);
            var integrated = submissionCounts.Where(s => s.Status == "Integrated").Sum(s => s.Count);

            var auditLogCount = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);

            ViewData["SourceCount"] = sources;
            ViewData["TotalBatches"] = totalPulled;
            ViewData["PendingVerify"] = pendingVerify;
            ViewData["SubmittedCount"] = submitted;
            ViewData["IntegratedCount"] = integrated;
            ViewData["AuditLogCount"] = auditLogCount;

            var recentSubmissions = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(10)
                .ToListAsync();
            ViewData["RecentSubmissions"] = recentSubmissions;

            var recentBatchData = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => orgSourceIds.Contains(r.DataSourceID))
                .GroupBy(r => new { r.BatchId, r.DataSourceID })
                .Select(g => new
                {
                    BatchId = g.Key.BatchId,
                    DataSourceID = g.Key.DataSourceID,
                    RowCount = g.Count(),
                    PulledAt = g.Min(r => r.Pulled_at),
                    HasSubmitted = g.Any(r => r.SubmissionID != null),
                    AllValid = g.All(r => r.ValidationStatus == StatusValid),
                    HasPending = g.Any(r => r.ValidationStatus == StatusPending)
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(10)
                .ToListAsync();

            var batchSourceIds = recentBatchData.Select(b => b.DataSourceID).Distinct().ToList();
            var sourceInfo = await _context.DataSources
                .Where(s => batchSourceIds.Contains(s.DataSourceID))
                .Select(s => new { s.DataSourceID, s.SourceName, s.TargetTable })
                .ToDictionaryAsync(s => s.DataSourceID);

            var recentBatches = recentBatchData.Select(b => new
            {
                b.BatchId,
                DataSourceName = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].SourceName : StatusUnknown,
                TargetTable = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].TargetTable : StatusUnknown,
                b.RowCount,
                b.PulledAt,
                Status = GetBatchStatus(b.HasSubmitted, b.AllValid, b.HasPending)
            }).ToList();
            ViewData["RecentBatches"] = recentBatches;
        }

        private async Task LoadUploadData()
        {
            var orgId = GetCurrentOrgId();
            var sources = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.Status == StatusActive && s.OrganizationID == orgId)
                .OrderBy(s => s.SourceName)
                .ToListAsync();
            ViewData["DataSources"] = sources;

            var orgSourceIds = sources.Select(s => s.DataSourceID).ToList();
            var pendingBatches = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => orgSourceIds.Contains(r.DataSourceID) && r.SubmissionID == null)
                .GroupBy(r => r.DataSourceID)
                .Select(g => new { DataSourceID = g.Key, BatchId = g.Min(r => r.BatchId), RowCount = g.Count() })
                .ToDictionaryAsync(x => x.DataSourceID);
            ViewData["PendingBatches"] = pendingBatches;

            var allOrgSourceIds = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .Select(s => s.DataSourceID)
                .ToListAsync();

            var recentBatchData = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => allOrgSourceIds.Contains(r.DataSourceID))
                .GroupBy(r => new { r.BatchId, r.DataSourceID })
                .Select(g => new
                {
                    BatchId = g.Key.BatchId,
                    DataSourceID = g.Key.DataSourceID,
                    RowCount = g.Count(),
                    PulledAt = g.Min(r => r.Pulled_at),
                    HasSubmitted = g.Any(r => r.SubmissionID != null),
                    AllValid = g.All(r => r.ValidationStatus == StatusValid),
                    HasPending = g.Any(r => r.ValidationStatus == StatusPending)
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(10)
                .ToListAsync();

            var batchSourceIds = recentBatchData.Select(b => b.DataSourceID).Distinct().ToList();
            var sourceInfo = await _context.DataSources
                .Where(s => batchSourceIds.Contains(s.DataSourceID))
                .Select(s => new { s.DataSourceID, s.SourceName, s.TargetTable })
                .ToDictionaryAsync(s => s.DataSourceID);

            var recentBatches = recentBatchData.Select(b => new
            {
                b.BatchId,
                DataSourceName = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].SourceName : StatusUnknown,
                TargetTable = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].TargetTable : StatusUnknown,
                b.RowCount,
                b.PulledAt,
                Status = GetBatchStatus(b.HasSubmitted, b.AllValid, b.HasPending)
            }).ToList();
            ViewData["RecentBatches"] = recentBatches;
        }

        private async Task LoadVerifyData(string? viewBatchId, int page)
        {
            var orgId = GetCurrentOrgId();
            const int pageSize = 20;
            var currentPage = Math.Max(page, 1);

            // Get the DataSource IDs that belong to this org (simple, fast query)
            var orgSourceIds = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .Select(s => s.DataSourceID)
                .ToListAsync();

            // Get batches that have pending or recently validated records
            var batches = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => r.SubmissionID == null && orgSourceIds.Contains(r.DataSourceID))
                .GroupBy(r => new { r.BatchId, r.DataSourceID })
                .Select(g => new
                {
                    BatchId = g.Key.BatchId,
                    DataSourceID = g.Key.DataSourceID,
                    TotalRows = g.Count(),
                    ValidRows = g.Count(r => r.ValidationStatus == StatusValid),
                    ErrorRows = g.Count(r => r.ValidationStatus == StatusError),
                    WarningRows = g.Count(r => r.ValidationStatus == StatusWarning),
                    PendingRows = g.Count(r => r.ValidationStatus == StatusPending),
                    PulledAt = g.Min(r => r.Pulled_at)
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(50)
                .ToListAsync();

            // Fetch source names in a separate lightweight query
            var sourceIds = batches.Select(b => b.DataSourceID).Distinct().ToList();
            var sourceInfo = await _context.DataSources
                .AsNoTracking()
                .Where(s => sourceIds.Contains(s.DataSourceID))
                .Select(s => new { s.DataSourceID, s.SourceName, s.TargetTable })
                .ToDictionaryAsync(s => s.DataSourceID);

            // Combine into final result
            var batchResults = batches.Select(b => new
            {
                b.BatchId,
                DataSourceName = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].SourceName : StatusUnknown,
                TargetTable = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].TargetTable : StatusUnknown,
                b.TotalRows,
                b.ValidRows,
                b.ErrorRows,
                b.WarningRows,
                b.PendingRows,
                b.PulledAt
            }).ToList();
            ViewData["VerifyBatches"] = batchResults;

            // If there's a specific batch to preview (from TempData or the first pending batch)
            var previewBatchId = viewBatchId
                                ?? TempData["ValidateBatchId"] as string
                                ?? TempData["PullBatchId"] as string
                                ?? batchResults.FirstOrDefault()?.BatchId;

            if (previewBatchId != null)
            {
                var totalRows = await _context.StagingRecords
                    .AsNoTracking()
                    .Where(r => r.BatchId == previewBatchId)
                    .CountAsync();

                var previewRecords = await _context.StagingRecords
                    .AsNoTracking()
                    .Where(r => r.BatchId == previewBatchId)
                    .OrderBy(r => r.RowNumber)
                    .Skip((currentPage - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                ViewData["PreviewRecords"] = previewRecords;
                ViewData["PreviewBatchId"] = previewBatchId;
                ViewData["PreviewPage"] = currentPage;
                ViewData["PreviewPageSize"] = pageSize;
                ViewData["PreviewTotalRows"] = totalRows;

                var batchInfo = batchResults.FirstOrDefault(b => b.BatchId == previewBatchId);
                ViewData["PreviewBatchInfo"] = batchInfo;
            }
        }

        private async Task LoadSubmitData()
        {
            var orgId = GetCurrentOrgId();

            var orgSourceIds = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .Select(s => s.DataSourceID)
                .ToListAsync();

            // Get verified batches ready for submission
            var readyBatchData = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => r.SubmissionID == null && r.ValidationStatus != StatusPending && orgSourceIds.Contains(r.DataSourceID))
                .GroupBy(r => new { r.BatchId, r.DataSourceID })
                .Select(g => new
                {
                    BatchId = g.Key.BatchId,
                    DataSourceID = g.Key.DataSourceID,
                    TotalRows = g.Count(),
                    ValidRows = g.Count(r => r.ValidationStatus == StatusValid),
                    ErrorRows = g.Count(r => r.ValidationStatus == StatusError),
                    PulledAt = g.Min(r => r.Pulled_at)
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(50)
                .ToListAsync();

            var batchSourceIds = readyBatchData.Select(b => b.DataSourceID).Distinct().ToList();
            var sourceInfo = await _context.DataSources
                .AsNoTracking()
                .Where(s => batchSourceIds.Contains(s.DataSourceID))
                .Select(s => new { s.DataSourceID, s.SourceName, s.TargetTable })
                .ToDictionaryAsync(s => s.DataSourceID);

            var readyBatches = readyBatchData.Select(b => new
            {
                b.BatchId,
                DataSourceName = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].SourceName : StatusUnknown,
                TargetTable = sourceInfo.ContainsKey(b.DataSourceID) ? sourceInfo[b.DataSourceID].TargetTable : StatusUnknown,
                b.TotalRows,
                b.ValidRows,
                b.ErrorRows,
                b.PulledAt
            }).ToList();
            ViewData["ReadyBatches"] = readyBatches;

            // Recent submissions
            var submissions = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(10)
                .ToListAsync();
            ViewData["Submissions"] = submissions;
        }

        private async Task LoadReportsData()
        {
            var orgId = GetCurrentOrgId();

            // Pre-fetch org source IDs to avoid repeated navigation property joins
            var orgSourceIds = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .Select(s => s.DataSourceID)
                .ToListAsync();

            // KPI totals
            ViewData["TotalSources"] = orgSourceIds.Count;
            ViewData["TotalBatches"] = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => orgSourceIds.Contains(r.DataSourceID))
                .Select(r => r.BatchId).Distinct().CountAsync();
            ViewData["TotalSubmissions"] = await _context.DataSubmissions.AsNoTracking().CountAsync(s => s.OrganizationID == orgId);
            ViewData["TotalStagingRows"] = await _context.StagingRecords
                .AsNoTracking()
                .CountAsync(r => orgSourceIds.Contains(r.DataSourceID));

            // Source summary
            var sourceSummary = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .Select(s => new
                {
                    s.SourceName,
                    s.TargetTable,
                    s.Status,
                    s.Last_sync,
                    BatchCount = s.StagingRecords.Select(r => r.BatchId).Distinct().Count(),
                    TotalRows = s.StagingRecords.Count
                })
                .OrderByDescending(s => s.TotalRows)
                .ToListAsync();
            ViewData["SourceSummary"] = sourceSummary;

            // Submission status breakdown
            var statusBreakdown = await _context.DataSubmissions
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Rows = g.Sum(s => s.TotalRows) })
                .ToListAsync();
            ViewData["StatusBreakdown"] = statusBreakdown;

            // Monthly submissions (last 12)
            var monthlySubmissions = await _context.DataSubmissions
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => new { s.Created_at.Year, s.Created_at.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count(),
                    Rows = g.Sum(s => s.TotalRows)
                })
                .OrderByDescending(g => g.Year).ThenByDescending(g => g.Month)
                .Take(12)
                .ToListAsync();
            ViewData["MonthlySubmissions"] = monthlySubmissions;

            // Monthly records pulled (last 6 months)
            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
            var monthlyRecordsPulled = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => orgSourceIds.Contains(r.DataSourceID) && r.Pulled_at >= sixMonthsAgo)
                .GroupBy(r => new { r.Pulled_at.Year, r.Pulled_at.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
                .OrderBy(g => g.Year).ThenBy(g => g.Month)
                .ToListAsync();
            ViewData["MonthlyRecordsPulled"] = monthlyRecordsPulled;

            // Validation summary
            var validationSummary = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => orgSourceIds.Contains(r.DataSourceID))
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["ValidationSummary"] = validationSummary;
        }

        private static string GetBatchStatus(bool hasSubmitted, bool allValid, bool hasPending)
        {
            if (hasSubmitted)
            {
                return StatusSubmitted;
            }

            if (allValid)
            {
                return StatusVerified;
            }

            if (hasPending)
            {
                return StatusPending;
            }

            return StatusHasIssues;
        }

        private async Task LoadSourcesData()
        {
            var orgId = GetCurrentOrgId();
            var sources = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .ToListAsync();
            ViewData["AllSources"] = sources;
        }

        private async Task LoadHistoryData()
        {
            var orgId = GetCurrentOrgId();
            var logs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .OrderByDescending(a => a.Performed_at)
                .Take(100)
                .ToListAsync();
            ViewData["AuditLogs"] = logs;

            ViewData["TotalActions"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);
            ViewData["TodayActions"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId && a.Performed_at.Date == DateTime.UtcNow.Date);
            ViewData["UniqueEntities"] = await _context.AuditLogs.AsNoTracking().Where(a => a.OrganizationID == orgId).Select(a => a.EntityName).Distinct().CountAsync();
        }
    }
}
