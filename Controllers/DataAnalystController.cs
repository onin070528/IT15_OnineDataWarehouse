using System.Security.Claims;
using System.Text.Json;
using it15_webproject_mvc.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Controllers
{
    [Authorize(Roles = "DataAnalyst")]
    public class DataAnalystController : BaseController
    {
        private readonly ApplicationDbContext _context;

        public DataAnalystController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> AnalystNav(string section = "dashboard", string? viewTable = null)
        {
            ViewData["Section"] = section.ToLower();
            ViewData["OrganizationName"] = GetCurrentOrgName();
            ViewData["ViewTable"] = viewTable;

            var orgId = GetCurrentOrgId();
            var org = await _context.Organizations.FindAsync(orgId);
            ViewData["SubscriptionPlan"] = org?.SubscriptionPlan ?? "Free";

            var userId = GetCurrentUserId();

            switch (section.ToLower())
            {
                case "dashboard":
                    await LoadDashboardData(userId);
                    break;
                case "security":
                    await LoadSecurityData(userId);
                    break;
                case "etl":
                    await LoadETLData(userId);
                    break;
                case "warehouse":
                    await LoadWarehouseData(userId, viewTable);
                    break;
                case "cleansing":
                    await LoadCleansingData(userId);
                    break;
                case "history":
                    await LoadHistoryData(userId);
                    break;
            }

            return View();
        }

        public IActionResult AnalystIndex() => View();
        public IActionResult AnalystSecurity() => View();
        public IActionResult AnalystETL() => View();
        public IActionResult AnalystWarehouse() => View();
        public IActionResult AnalystCleansing() => View();
        public IActionResult AnalystHistory() => View();

        private async Task LoadDashboardData(int userId)
        {
            var orgId = GetCurrentOrgId();

            // Consolidate user counts
            var userCounts = await _context.Users.AsNoTracking()
                .Where(u => u.OrganizationID == orgId)
                .GroupBy(u => u.Account_status == "Active")
                .Select(g => new { IsActive = g.Key, Count = g.Count() })
                .ToListAsync();
            var totalUsers = userCounts.Sum(u => u.Count);
            ViewData["TotalUsers"] = totalUsers;
            ViewData["ActiveUsers"] = userCounts.Where(u => u.IsActive).Sum(u => u.Count);
            ViewData["InactiveUsers"] = userCounts.Where(u => !u.IsActive).Sum(u => u.Count);
            ViewData["TodayLogins"] = await _context.Users.AsNoTracking().CountAsync(u => u.OrganizationID == orgId && u.Last_login.HasValue && u.Last_login.Value.Date == DateTime.UtcNow.Date);

            // Consolidate source counts
            var sourceCounts = await _context.DataSources.AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => s.Status == "Active")
                .Select(g => new { IsActive = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalSources"] = sourceCounts.Sum(s => s.Count);
            ViewData["ActiveSources"] = sourceCounts.Where(s => s.IsActive).Sum(s => s.Count);

            // Consolidate submission counts
            var submissionCounts = await _context.DataSubmissions.AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalSubmissions"] = submissionCounts.Sum(s => s.Count);
            ViewData["PendingSubmissions"] = submissionCounts.Where(s => s.Status == "Submitted").Sum(s => s.Count);
            ViewData["IntegratedSubmissions"] = submissionCounts.Where(s => s.Status == "Integrated").Sum(s => s.Count);
            ViewData["FailedSubmissions"] = submissionCounts.Where(s => s.Status == "Failed").Sum(s => s.Count);

            // Warehouse metrics
            ViewData["TotalWarehouseRows"] = await _context.WarehouseRecords.AsNoTracking().CountAsync(w => w.OrganizationID == orgId && w.RecordStatus == "Active");

            // History metrics
            ViewData["TotalAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);
            ViewData["TodayAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId && a.Performed_at.Date == DateTime.UtcNow.Date);

            // Recent audit logs
            var recentLogs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .OrderByDescending(a => a.Performed_at)
                .Take(5)
                .ToListAsync();
            ViewData["RecentLogs"] = recentLogs;
        }

        private async Task LoadSecurityData(int userId)
        {
            var orgId = GetCurrentOrgId();
            var users = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Include(u => u.Organization)
                .Where(u => u.OrganizationID == orgId)
                .OrderByDescending(u => u.Last_login)
                .ToListAsync();
            ViewData["AllUsers"] = users;

            var totalUsers = users.Count;
            var activeUsers = users.Count(u => u.Account_status == "Active");
            var inactiveUsers = users.Count(u => u.Account_status != "Active");
            var recentLogins = users.Count(u => u.Last_login.HasValue && u.Last_login.Value >= DateTime.UtcNow.AddHours(-24));

            ViewData["TotalUsers"] = totalUsers;
            ViewData["ActiveUsers"] = activeUsers;
            ViewData["InactiveUsers"] = inactiveUsers;
            ViewData["RecentLogins"] = recentLogins;

            // Security-related audit logs
            var securityLogs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId && (a.EntityType == "User" || a.Action.Contains("Login") || a.Action.Contains("User")))
                .OrderByDescending(a => a.Performed_at)
                .Take(50)
                .ToListAsync();
            ViewData["SecurityLogs"] = securityLogs;
        }

        private async Task LoadETLData(int userId)
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

            // Batch summary
            var batches = await _context.StagingRecords
                .AsNoTracking()
                .Include(r => r.DataSource)
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => new { r.BatchId, r.DataSourceID })
                .Select(g => new
                {
                    BatchId = g.Key.BatchId,
                    DataSourceName = g.First().DataSource!.SourceName,
                    TargetTable = g.First().DataSource!.TargetTable,
                    TotalRows = g.Count(),
                    ValidRows = g.Count(r => r.ValidationStatus == "Valid"),
                    ErrorRows = g.Count(r => r.ValidationStatus == "Error"),
                    PendingRows = g.Count(r => r.ValidationStatus == "Pending"),
                    PulledAt = g.Min(r => r.Pulled_at),
                    Status = g.Any(r => r.SubmissionID != null) ? "Submitted" :
                             g.All(r => r.ValidationStatus == "Valid") ? "Verified" :
                             g.Any(r => r.ValidationStatus == "Pending") ? "Pending" : "Has Issues"
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(20)
                .ToListAsync();
            ViewData["AllBatches"] = batches;

            var totalSources = sources.Count;
            var activeSources = sources.Count(s => s.Status == "Active");
            var totalSubmissions = submissions.Count;
            var integratedCount = submissions.Count(s => s.Status == "Integrated");

            ViewData["TotalSources"] = totalSources;
            ViewData["ActiveSources"] = activeSources;
            ViewData["TotalSubmissions"] = totalSubmissions;
            ViewData["IntegratedCount"] = integratedCount;
        }

        private async Task LoadWarehouseData(int userId, string? viewTable)
        {
            var orgId = GetCurrentOrgId();

            // Warehouse table summary
            var warehouseSummary = await _context.WarehouseRecords
                .AsNoTracking()
                .Include(w => w.DataSource)
                .Where(w => w.OrganizationID == orgId && w.RecordStatus == "Active")
                .GroupBy(w => new { w.TargetTable, w.DataSourceID })
                .Select(g => new
                {
                    SourceName = g.First().DataSource!.SourceName,
                    TargetTable = g.Key.TargetTable,
                    LastLoaded = g.Max(w => w.Loaded_at),
                    TotalRows = g.Count(),
                    LatestVersion = g.Max(w => w.Version)
                })
                .OrderByDescending(s => s.TotalRows)
                .ToListAsync();
            ViewData["WarehouseSummary"] = warehouseSummary;

            ViewData["TotalTables"] = warehouseSummary.Select(s => s.TargetTable).Distinct().Count();
            ViewData["TotalWarehouseRows"] = warehouseSummary.Sum(s => s.TotalRows);

            // Table names for dropdown
            var tableNames = warehouseSummary.Select(s => (string)s.TargetTable).Distinct().ToList();
            ViewData["TableNames"] = tableNames;

            // If a specific table is selected, load its data rows
            if (!string.IsNullOrEmpty(viewTable) && tableNames.Contains(viewTable))
            {
                var warehouseRows = await _context.WarehouseRecords
                    .AsNoTracking()
                    .Where(w => w.OrganizationID == orgId && w.TargetTable == viewTable && w.RecordStatus == "Active")
                    .OrderByDescending(w => w.Loaded_at)
                    .ThenBy(w => w.RowNumber)
                    .Take(100)
                    .ToListAsync();

                var allColumns = new List<string>();
                var parsedRows = new List<Dictionary<string, string>>();

                foreach (var row in warehouseRows)
                {
                    try
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.CleanData);
                        if (dict == null) continue;

                        var rowData = new Dictionary<string, string>();
                        foreach (var kvp in dict)
                        {
                            if (!allColumns.Contains(kvp.Key))
                                allColumns.Add(kvp.Key);

                            rowData[kvp.Key] = kvp.Value.ValueKind switch
                            {
                                JsonValueKind.String => kvp.Value.GetString() ?? "",
                                JsonValueKind.Number => kvp.Value.GetRawText(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => "",
                                _ => kvp.Value.GetRawText()
                            };
                        }
                        parsedRows.Add(rowData);
                    }
                    catch { }
                }

                ViewData["WarehouseColumns"] = allColumns;
                ViewData["WarehouseRows"] = parsedRows;
                ViewData["WarehouseRowCount"] = warehouseRows.Count;
                ViewData["SelectedTable"] = viewTable;
            }

            // Source field mapping overview
            var fieldMappings = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId && s.Status == "Active")
                .Select(s => new { s.SourceName, s.TargetTable, s.ApiBaseUrl, s.ApiEndpoint })
                .ToListAsync();
            ViewData["FieldMappings"] = fieldMappings;
        }

        private async Task LoadCleansingData(int userId)
        {
            var orgId = GetCurrentOrgId();

            // Consolidate staging validation stats
            var cleansingStats = await _context.StagingRecords.AsNoTracking()
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalRows = cleansingStats.Sum(s => s.Count);
            var validRows = cleansingStats.Where(s => s.Status == "Valid").Sum(s => s.Count);
            var errorRows = cleansingStats.Where(s => s.Status == "Error").Sum(s => s.Count);
            var warningRows = cleansingStats.Where(s => s.Status == "Warning").Sum(s => s.Count);
            var pendingRows = cleansingStats.Where(s => s.Status == "Pending").Sum(s => s.Count);

            ViewData["TotalRows"] = totalRows;
            ViewData["ValidRows"] = validRows;
            ViewData["ErrorRows"] = errorRows;
            ViewData["WarningRows"] = warningRows;
            ViewData["PendingRows"] = pendingRows;
            ViewData["SuccessRate"] = totalRows > 0 ? Math.Round(validRows * 100.0 / totalRows, 1) : 0.0;

            // Batch quality breakdown
            var batches = await _context.StagingRecords
                .AsNoTracking()
                .Include(r => r.DataSource)
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => new { r.BatchId, r.DataSourceID })
                .Select(g => new
                {
                    BatchId = g.Key.BatchId,
                    DataSourceName = g.First().DataSource!.SourceName,
                    TargetTable = g.First().DataSource!.TargetTable,
                    TotalRows = g.Count(),
                    ValidRows = g.Count(r => r.ValidationStatus == "Valid"),
                    ErrorRows = g.Count(r => r.ValidationStatus == "Error"),
                    WarningRows = g.Count(r => r.ValidationStatus == "Warning"),
                    PendingRows = g.Count(r => r.ValidationStatus == "Pending"),
                    PulledAt = g.Min(r => r.Pulled_at)
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(30)
                .ToListAsync();
            ViewData["AllBatches"] = batches;

            // Quality per source
            var qualityBySource = await _context.StagingRecords.AsNoTracking()
                .Include(r => r.DataSource)
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => r.DataSource!.SourceName)
                .Select(g => new
                {
                    SourceName = g.Key,
                    TotalRows = g.Count(),
                    ValidRows = g.Count(r => r.ValidationStatus == "Valid"),
                    ErrorRows = g.Count(r => r.ValidationStatus == "Error"),
                    WarningRows = g.Count(r => r.ValidationStatus == "Warning")
                })
                .OrderByDescending(g => g.TotalRows)
                .ToListAsync();
            ViewData["QualityBySource"] = qualityBySource;

            // Common validation issues
            var commonIssues = await _context.StagingRecords.AsNoTracking()
                .Where(r => r.DataSource!.OrganizationID == orgId && r.ValidationMessage != null && r.ValidationMessage != "")
                .GroupBy(r => r.ValidationMessage!)
                .Select(g => new { Message = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToListAsync();
            ViewData["CommonIssues"] = commonIssues;
        }

        private async Task LoadHistoryData(int userId)
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
            ViewData["UniqueUsers"] = await _context.AuditLogs.AsNoTracking().Where(a => a.OrganizationID == orgId).Select(a => a.PerformedByUserID).Distinct().CountAsync();

            // Action breakdown
            var actionBreakdown = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.OrganizationID == orgId)
                .GroupBy(a => a.Action)
                .Select(g => new { Action = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();
            ViewData["ActionBreakdown"] = actionBreakdown;

            // Version history for trend analysis
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
                    ActiveRecords = g.Count(w => w.RecordStatus == "Active"),
                    ArchivedRecords = g.Count(w => w.RecordStatus == "Archived")
                })
                .OrderByDescending(g => g.LastUpdated)
                .ToListAsync();
            ViewData["VersionHistory"] = versionHistory;

            // Monthly activity trend (last 6 months)
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

            // Monthly records pulled
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
        }
    }
}
