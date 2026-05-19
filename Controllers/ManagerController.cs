using System.Security.Claims;
using System.Text.Json;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;
using it15_webproject_mvc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ManagerController : BaseController
    {
        private const string StatusActive = "Active";
        private const string StatusArchived = "Archived";
        private const string StatusIntegrated = "Integrated";
        private const string StatusSubmitted = "Submitted";
        private const string StatusFailed = "Failed";
        private const string StatusValid = "Valid";
        private const string StatusError = "Error";
        private const string LoadModeOverwrite = "Overwrite";
        private const string LoadModeUpsert = "Upsert";
        private const string NotificationTypeSuccess = "Success";
        private const string NotificationTypeError = "Error";
        private const string TempDataErrorKey = "Error";
        private const string TempDataSuccessKey = "Success";

        private readonly ApplicationDbContext _context;
        private readonly IAuditService _audit;
        private readonly IDataCleansingService _cleanser;
        private readonly INotificationService _notif;
        private readonly ILogger<ManagerController> _logger;

        public ManagerController(ApplicationDbContext context, IAuditService audit, IDataCleansingService cleanser, INotificationService notif, ILogger<ManagerController> logger)
        {
            _context = context;
            _audit = audit;
            _cleanser = cleanser;
            _notif = notif;
            _logger = logger;
        }

        public async Task<IActionResult> ManagerNav(string section = "dashboard")
        {
            ViewData["Section"] = section.ToLower();
            ViewData["OrganizationName"] = GetCurrentOrgName();

            var orgId = GetCurrentOrgId();
            var org = await _context.Organizations.FindAsync(orgId);
            ViewData["SubscriptionPlan"] = org?.SubscriptionPlan ?? "Free";

            switch (section.ToLower())
            {
                case "dashboard":
                    await LoadDashboardData();
                    break;
                case "viewdata":
                    await LoadViewDataSection();
                    break;
                case "reports":
                    await LoadReportsData();
                    break;
                case "history":
                    await LoadHistoryData();
                    break;
                case "export":
                    await LoadExportData();
                    break;
                case "performance":
                    await LoadPerformanceData();
                    break;
                case "approvals":
                    await LoadApprovalsData();
                    break;
            }

            return View();
        }

        // Partial view actions
        public IActionResult ManagerIndex() => View();
        public IActionResult ManagerViewData() => View();
        public IActionResult ManagerReports() => View();
        public IActionResult ManagerHistory() => View();
        public IActionResult ManagerExport() => View();
        public IActionResult ManagerPerformance() => View();
        public IActionResult ManagerApprovals() => View();

        // === APPROVE / REJECT submission ===
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSubmission(int submissionId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var userId = GetCurrentUserId();
            var submission = await _context.DataSubmissions
                .Include(s => s.DataSource)
                .FirstOrDefaultAsync(s => s.SubmissionID == submissionId && s.OrganizationID == orgId);
            if (submission == null) return NotFound();

            var approvalResult = await ProcessApprovalAsync(submission, orgId, userId);
            if (!approvalResult.Success)
            {
                TempData[TempDataErrorKey] = approvalResult.ErrorMessage;
            }
            else
            {
                TempData[TempDataSuccessKey] = $"Submission {submission.BatchId} approved - {approvalResult.LoadedCount} rows loaded into warehouse table '{submission.TargetTable}'.";
            }

            return RedirectToAction("ManagerNav", new { section = "approvals" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectSubmission(int submissionId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var orgId = GetCurrentOrgId();
            var submission = await _context.DataSubmissions.FirstOrDefaultAsync(s => s.SubmissionID == submissionId && s.OrganizationID == orgId);
            if (submission == null) return NotFound();

            submission.Status = StatusFailed;

            _audit.Log("Submission Rejected", "DataSubmission", submission.SubmissionID, submission.BatchId,
                $"Rejected by Manager | Target: {submission.TargetTable} | Rows: {submission.ValidRows}/{submission.TotalRows}",
                GetCurrentUserId(), orgId);
            await _context.SaveChangesAsync();

            TempData[TempDataErrorKey] = $"Submission {submission.BatchId} has been rejected.";

            // Notify the staff who submitted
            _notif.Notify(submission.SubmittedByUserID, orgId,
                "Submission Rejected",
                $"Your batch {submission.BatchId} targeting '{submission.TargetTable}' was rejected by a Manager.",
                NotificationTypeError,
                "/Staff/StaffNav?section=submit");
            await _context.SaveChangesAsync();

            return RedirectToAction("ManagerNav", new { section = "approvals" });
        }

        // === DATA LOADERS ===

        private async Task LoadDashboardData()
        {
            var orgId = GetCurrentOrgId();
            ViewData["TotalSources"] = await _context.DataSources.AsNoTracking().CountAsync(s => s.OrganizationID == orgId);
            ViewData["ActiveSources"] = await _context.DataSources.AsNoTracking().CountAsync(s => s.Status == StatusActive && s.OrganizationID == orgId);

            // Consolidate submission counts into a single round-trip
            var submissionCounts = await _context.DataSubmissions
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalSubmissions"] = submissionCounts.Sum(s => s.Count);
            ViewData["PendingApprovals"] = submissionCounts.Where(s => s.Status == StatusSubmitted).Sum(s => s.Count);
            ViewData["IntegratedCount"] = submissionCounts.Where(s => s.Status == StatusIntegrated).Sum(s => s.Count);
            ViewData["FailedCount"] = submissionCounts.Where(s => s.Status == StatusFailed).Sum(s => s.Count);

            ViewData["TotalAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);
            ViewData["TodayAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId && a.Performed_at.Date == DateTime.UtcNow.Date);

            var recentLogs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .OrderByDescending(a => a.Performed_at)
                .Take(5)
                .ToListAsync();
            ViewData["RecentLogs"] = recentLogs;

            var pendingList = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.Status == StatusSubmitted && s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(5)
                .ToListAsync();
            ViewData["PendingList"] = pendingList;
        }

        private async Task LoadViewDataSection()
        {
            var orgId = GetCurrentOrgId();
            var sources = await _context.DataSources
                .AsNoTracking()
                .Include(s => s.CreatedByUser)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .ToListAsync();
            ViewData["AllSources"] = sources;

            var submissions = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(50)
                .ToListAsync();
            ViewData["AllSubmissions"] = submissions;

            ViewData["TotalSources"] = sources.Count;
            ViewData["TotalSubmissions"] = submissions.Count;
            ViewData["TotalRowsLoaded"] = submissions.Sum(s => s.ValidRows);
        }

        private async Task LoadReportsData()
        {
            var orgId = GetCurrentOrgId();
            // Source summary for reports
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

            // Monthly submission counts
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
        }

        private async Task LoadHistoryData()
        {
            var orgId = GetCurrentOrgId();

            // Audit logs
            var logs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .OrderByDescending(a => a.Performed_at)
                .Take(100)
                .ToListAsync();
            ViewData["AuditLogs"] = logs;

            // Summary cards
            ViewData["TotalActions"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);
            ViewData["TodayActions"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId && a.Performed_at.Date == DateTime.UtcNow.Date);
            ViewData["TotalDatasets"] = await _context.DataSources.AsNoTracking().CountAsync(s => s.OrganizationID == orgId);
            ViewData["TotalRecordsStored"] = await _context.WarehouseRecords.AsNoTracking().CountAsync(w => w.OrganizationID == orgId && w.RecordStatus == StatusActive);
            ViewData["UniqueEntities"] = await _context.AuditLogs.AsNoTracking().Where(a => a.OrganizationID == orgId).Select(a => a.EntityName).Distinct().CountAsync();
            ViewData["UniqueUsers"] = await _context.AuditLogs.AsNoTracking().Where(a => a.OrganizationID == orgId).Select(a => a.PerformedByUserID).Distinct().CountAsync();

            // Data Version History — warehouse records with version > 1 or all latest per target table
            var versionHistory = await _context.WarehouseRecords
                .AsNoTracking()
                .Include(w => w.LoadedByUser)
                .Include(w => w.DataSource)
                .Where(w => w.OrganizationID == orgId)
                .GroupBy(w => new { w.TargetTable, w.DataSourceID })
                .Select(g => new
                {
                    TargetTable = g.Key.TargetTable,
                    DataSourceName = g.First().DataSource!.SourceName,
                    LatestVersion = g.Max(w => w.Version),
                    TotalVersions = g.Select(w => w.Version).Distinct().Count(),
                    LastUpdated = g.Max(w => w.Loaded_at),
                    LastUpdatedBy = g.OrderByDescending(w => w.Loaded_at).First().LoadedByUser!.Full_name,
                    ActiveRecords = g.Count(w => w.RecordStatus == StatusActive),
                    ArchivedRecords = g.Count(w => w.RecordStatus == StatusArchived)
                })
                .OrderByDescending(g => g.LastUpdated)
                .ToListAsync();
            ViewData["VersionHistory"] = versionHistory;

            // Dataset Snapshots — submissions that have been integrated
            var snapshots = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.OrganizationID == orgId && s.Status == StatusIntegrated)
                .OrderByDescending(s => s.Integrated_at)
                .Take(20)
                .Select(s => new
                {
                    s.SubmissionID,
                    s.BatchId,
                    SourceName = s.DataSource!.SourceName,
                    s.TargetTable,
                    s.LoadedRows,
                    s.SkippedRows,
                    s.TotalRows,
                    s.LoadMode,
                    s.Integrated_at,
                    SubmittedBy = s.SubmittedByUser!.Full_name
                })
                .ToListAsync();
            ViewData["DatasetSnapshots"] = snapshots;

            // ETL Pipeline Lifecycle — recent submissions with all timestamps
            var pipelineLifecycle = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(10)
                .ToListAsync();
            ViewData["PipelineLifecycle"] = pipelineLifecycle;

            // Monthly activity trend (last 6 months) for charts
            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
            var monthlyActivity = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.OrganizationID == orgId && a.Performed_at >= sixMonthsAgo)
                .GroupBy(a => new { a.Performed_at.Year, a.Performed_at.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
                .OrderBy(g => g.Year).ThenBy(g => g.Month)
                .ToListAsync();
            ViewData["MonthlyActivity"] = monthlyActivity;

            // Records pulled over time (monthly)
            var monthlyRecordsPulled = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => r.DataSource!.OrganizationID == orgId && r.Pulled_at >= sixMonthsAgo)
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

            // Sources added over time (monthly)
            var monthlySourcesAdded = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId && s.Created_at >= sixMonthsAgo)
                .GroupBy(s => new { s.Created_at.Year, s.Created_at.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
                .OrderBy(g => g.Year).ThenBy(g => g.Month)
                .ToListAsync();
            ViewData["MonthlySourcesAdded"] = monthlySourcesAdded;

            // Entity type distribution for filtering
            var entityTypes = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.OrganizationID == orgId)
                .GroupBy(a => a.EntityType)
                .Select(g => new { EntityType = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();
            ViewData["EntityTypes"] = entityTypes;
        }

        private async Task LoadExportData()
        {
            var orgId = GetCurrentOrgId();
            var submissions = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => (s.Status == StatusIntegrated || s.Status == StatusSubmitted) && s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .ToListAsync();
            ViewData["ExportableSubmissions"] = submissions;

            ViewData["TotalExportable"] = submissions.Count;
            ViewData["TotalExportableRows"] = submissions.Sum(s => s.ValidRows);
        }

        private async Task LoadPerformanceData()
        {
            var orgId = GetCurrentOrgId();
            // Source performance
            var sourcePerf = await _context.DataSources
                .AsNoTracking()
                .Include(s => s.CreatedByUser)
                .Where(s => s.OrganizationID == orgId)
                .Select(s => new
                {
                    s.SourceName,
                    s.Status,
                    s.Created_at,
                    s.Last_sync,
                    TotalBatches = s.StagingRecords.Select(r => r.BatchId).Distinct().Count(),
                    TotalRows = s.StagingRecords.Count,
                    ValidRows = s.StagingRecords.Count(r => r.ValidationStatus == StatusValid),
                    ErrorRows = s.StagingRecords.Count(r => r.ValidationStatus == StatusError)
                })
                .OrderByDescending(s => s.TotalRows)
                .ToListAsync();
            ViewData["SourcePerformance"] = sourcePerf;

            // User activity
            var userActivity = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .GroupBy(a => new { a.PerformedByUserID, Name = a.PerformedByUser!.Full_name })
                .Select(g => new
                {
                    UserName = g.Key.Name,
                    ActionCount = g.Count(),
                    LastAction = g.Max(a => a.Performed_at)
                })
                .OrderByDescending(g => g.ActionCount)
                .Take(10)
                .ToListAsync();
            ViewData["UserActivity"] = userActivity;

            // Overall stats - consolidate staging record counts into single query
            ViewData["TotalSources"] = await _context.DataSources.AsNoTracking().CountAsync(s => s.OrganizationID == orgId);

            var stagingStats = await _context.StagingRecords
                .AsNoTracking()
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var totalRows = stagingStats.Sum(s => s.Count);
            var validRows = stagingStats.Where(s => s.Status == StatusValid).Sum(s => s.Count);
            var errorRows = stagingStats.Where(s => s.Status == StatusError).Sum(s => s.Count);
            ViewData["TotalRows"] = totalRows;
            ViewData["ValidRows"] = validRows;
            ViewData["ErrorRows"] = errorRows;
            ViewData["SuccessRate"] = totalRows > 0
                ? Math.Round(validRows * 100.0 / totalRows, 1)
                : 0.0;
        }

        private async Task LoadApprovalsData()
        {
            var orgId = GetCurrentOrgId();
            var pending = await _context.DataSubmissions
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.Status == StatusSubmitted && s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .ToListAsync();
            ViewData["PendingSubmissions"] = pending;

            // Load staging records for each pending submission so Manager can preview the data
            var previewData = await BuildPreviewDataAsync(pending);
            ViewData["PreviewData"] = previewData;

            var recent = await _context.DataSubmissions
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => (s.Status == StatusIntegrated || s.Status == StatusFailed) && s.OrganizationID == orgId)
                .OrderByDescending(s => s.Submitted_at)
                .Take(20)
                .ToListAsync();
            ViewData["RecentDecisions"] = recent;

            ViewData["PendingCount"] = pending.Count;

            // Consolidate approved/rejected counts into single query
            var decisionCounts = await _context.DataSubmissions
                .AsNoTracking()
                .Where(s => (s.Status == StatusIntegrated || s.Status == StatusFailed) && s.OrganizationID == orgId)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["ApprovedCount"] = decisionCounts.Where(s => s.Status == StatusIntegrated).Sum(s => s.Count);
            ViewData["RejectedCount"] = decisionCounts.Where(s => s.Status == StatusFailed).Sum(s => s.Count);
        }

        private async Task<Dictionary<int, (List<string> Columns, List<Dictionary<string, string>> Rows)>> BuildPreviewDataAsync(
            IReadOnlyCollection<DataSubmission> pending)
        {
            var pendingIds = pending.Select(p => p.SubmissionID).ToList();
            if (pendingIds.Count == 0)
            {
                return new Dictionary<int, (List<string> Columns, List<Dictionary<string, string>> Rows)>();
            }

            var previewRecords = await _context.StagingRecords
                .Where(r => r.SubmissionID != null && pendingIds.Contains(r.SubmissionID.Value))
                .OrderBy(r => r.SubmissionID)
                .ThenBy(r => r.RowNumber)
                .ToListAsync();

            return BuildPreviewData(previewRecords);
        }

        private Dictionary<int, (List<string> Columns, List<Dictionary<string, string>> Rows)> BuildPreviewData(
            IEnumerable<StagingRecord> previewRecords)
        {
            var previewData = new Dictionary<int, (List<string> Columns, List<Dictionary<string, string>> Rows)>();

            foreach (var group in previewRecords.GroupBy(r => r.SubmissionID!.Value))
            {
                var columns = new List<string>();
                var rows = new List<Dictionary<string, string>>();

                foreach (var record in group.Take(20))
                {
                    if (TryParsePreviewRow(record, columns, out var rowData))
                    {
                        rows.Add(rowData);
                    }
                }

                previewData[group.Key] = (columns, rows);
            }

            return previewData;
        }

        private bool TryParsePreviewRow(
            StagingRecord record,
            List<string> columns,
            out Dictionary<string, string> rowData)
        {
            rowData = new Dictionary<string, string>
            {
                ["_status"] = record.ValidationStatus,
                ["_message"] = record.ValidationMessage ?? string.Empty
            };

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(record.RawData);
                if (dict == null)
                {
                    return false;
                }

                foreach (var kvp in dict)
                {
                    if (!columns.Contains(kvp.Key))
                    {
                        columns.Add(kvp.Key);
                    }

                    rowData[kvp.Key] = kvp.Value.ValueKind switch
                    {
                        JsonValueKind.String => kvp.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => kvp.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "(null)",
                        _ => kvp.Value.GetRawText()
                    };
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse preview record {RecordId}", record.StagingRecordID);
                return false;
            }
        }

        private async Task<ApprovalResult> ProcessApprovalAsync(DataSubmission submission, int orgId, int userId)
        {
            var result = new ApprovalResult(false, 0, "Failed to load submission. Please try again.");
            var strategy = _context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var stagingRecords = await GetValidStagingRecordsAsync(submission.SubmissionID);
                    var upsertCandidates = await BuildUpsertCandidatesAsync(submission, orgId);

                    var counts = ProcessStagingRecords(submission, stagingRecords, upsertCandidates, orgId, userId);

                    UpdateSubmissionAfterApproval(submission, counts.LoadedCount, counts.SkippedCount);
                    NotifySubmissionApproved(submission, orgId, counts.LoadedCount);
                    LogSubmissionApproved(submission, userId, orgId, counts.LoadedCount, counts.SkippedCount);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    result = new ApprovalResult(true, counts.LoadedCount, null);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Failed to approve submission {BatchId} (ID: {SubmissionId})", submission.BatchId, submission.SubmissionID);
                    result = new ApprovalResult(false, 0, $"Failed to load submission {submission.BatchId}. All changes have been rolled back. Reason: {ex.InnerException?.Message ?? ex.Message}");
                }
            });

            return result;
        }

        private async Task<List<StagingRecord>> GetValidStagingRecordsAsync(int submissionId)
        {
            return await _context.StagingRecords
                .Where(r => r.SubmissionID == submissionId && r.ValidationStatus == StatusValid)
                .OrderBy(r => r.RowNumber)
                .ToListAsync();
        }

        private async Task<Dictionary<string, WarehouseRecord>?> BuildUpsertCandidatesAsync(DataSubmission submission, int orgId)
        {
            if (submission.LoadMode != LoadModeUpsert)
            {
                return null;
            }

            var candidates = await _context.WarehouseRecords
                .Where(w => w.TargetTable == submission.TargetTable
                    && w.OrganizationID == orgId
                    && w.RecordStatus == StatusActive)
                .ToListAsync();

            var lookup = new Dictionary<string, WarehouseRecord>();
            foreach (var candidate in candidates)
            {
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(candidate.CleanData);
                    if (dict != null && dict.TryGetValue("id", out var existingId))
                    {
                        var key = existingId.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            lookup[key] = candidate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse clean data for warehouse record {RecordId}", candidate.WarehouseRecordID);
                }
            }

            return lookup;
        }

        private (int LoadedCount, int SkippedCount) ProcessStagingRecords(
            DataSubmission submission,
            List<StagingRecord> stagingRecords,
            Dictionary<string, WarehouseRecord>? upsertCandidates,
            int orgId,
            int userId)
        {
            var loadedCount = 0;
            var skippedCount = 0;

            if (submission.LoadMode == LoadModeOverwrite)
            {
                ArchiveExistingRecords(submission.TargetTable, orgId);
            }

            foreach (var staging in stagingRecords)
            {
                var cleanDict = _cleanser.CleanseRow(staging.RawData);
                if (cleanDict == null)
                {
                    skippedCount++;
                    continue;
                }

                if (TryUpsertRecord(submission, staging, cleanDict, upsertCandidates, orgId, userId))
                {
                    loadedCount++;
                    continue;
                }

                _context.WarehouseRecords.Add(CreateWarehouseRecord(submission, staging, cleanDict, orgId, userId, version: 1));
                loadedCount++;
            }

            return (loadedCount, skippedCount);
        }

        private void ArchiveExistingRecords(string targetTable, int orgId)
        {
            var existingRecords = _context.WarehouseRecords
                .Where(w => w.TargetTable == targetTable && w.OrganizationID == orgId && w.RecordStatus == StatusActive)
                .ToList();

            foreach (var existing in existingRecords)
            {
                existing.RecordStatus = StatusArchived;
            }
        }

        private bool TryUpsertRecord(
            DataSubmission submission,
            StagingRecord staging,
            Dictionary<string, object?> cleanDict,
            Dictionary<string, WarehouseRecord>? upsertCandidates,
            int orgId,
            int userId)
        {
            if (submission.LoadMode != LoadModeUpsert || upsertCandidates == null || !cleanDict.ContainsKey("id"))
            {
                return false;
            }

            var idValue = cleanDict["id"]?.ToString();
            if (string.IsNullOrEmpty(idValue) || !upsertCandidates.TryGetValue(idValue, out var existingRecord))
            {
                return false;
            }

            existingRecord.RecordStatus = StatusArchived;
            var newVersion = existingRecord.Version + 1;
            _context.WarehouseRecords.Add(CreateWarehouseRecord(submission, staging, cleanDict, orgId, userId, newVersion));
            return true;
        }

        private WarehouseRecord CreateWarehouseRecord(
            DataSubmission submission,
            StagingRecord staging,
            Dictionary<string, object?> cleanDict,
            int orgId,
            int userId,
            int version)
        {
            return new WarehouseRecord
            {
                DataSourceID = staging.DataSourceID,
                SubmissionID = submission.SubmissionID,
                BatchId = submission.BatchId,
                TargetTable = submission.TargetTable,
                RowNumber = staging.RowNumber,
                CleanData = JsonSerializer.Serialize(cleanDict),
                RawDataSnapshot = staging.RawData,
                RecordStatus = StatusActive,
                LoadMode = submission.LoadMode,
                Version = version,
                Loaded_at = DateTime.UtcNow,
                LoadedByUserID = userId,
                OrganizationID = orgId
            };
        }

        private static void UpdateSubmissionAfterApproval(DataSubmission submission, int loadedCount, int skippedCount)
        {
            submission.Status = StatusIntegrated;
            submission.Integrated_at = DateTime.UtcNow;
            submission.LoadedRows = loadedCount;
            submission.SkippedRows = skippedCount;
        }

        private void NotifySubmissionApproved(DataSubmission submission, int orgId, int loadedCount)
        {
            _notif.Notify(submission.SubmittedByUserID, orgId,
                "Submission Approved",
                $"Your batch {submission.BatchId} has been approved. {loadedCount} rows loaded into '{submission.TargetTable}'.",
                NotificationTypeSuccess,
                "/Staff/StaffNav?section=submit");
        }

        private void LogSubmissionApproved(DataSubmission submission, int userId, int orgId, int loadedCount, int skippedCount)
        {
            _audit.Log("Submission Approved & Loaded", "DataSubmission", submission.SubmissionID, submission.BatchId,
                $"Approved by Manager | Target: {submission.TargetTable} | Loaded: {loadedCount} rows | Skipped: {skippedCount} | Mode: {submission.LoadMode}",
                userId, orgId);
        }

        private sealed record ApprovalResult(bool Success, int LoadedCount, string? ErrorMessage);
    }
}
