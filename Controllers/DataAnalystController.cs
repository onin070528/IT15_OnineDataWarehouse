using System.Security.Claims;
using System.Text.Json;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Services;
using static it15_webproject_mvc.Constants.StatusConstants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace it15_webproject_mvc.Controllers
{
    [Authorize(Roles = "DataAnalyst")]
    public class DataAnalystController : BaseController
    {
        private readonly ApplicationDbContext _context;
        private readonly WarehouseSummaryService _warehouseSummaryService;
        private readonly WarehouseTableService _warehouseTableService;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<DataAnalystController> _logger;

        public DataAnalystController(
            ApplicationDbContext context,
            WarehouseSummaryService warehouseSummaryService,
            WarehouseTableService warehouseTableService,
            SubscriptionService subscriptionService,
            ILogger<DataAnalystController> logger)
        {
            _context = context;
            _warehouseSummaryService = warehouseSummaryService;
            _warehouseTableService = warehouseTableService;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        public async Task<IActionResult> AnalystNav(string section = "dashboard", string? viewTable = null)
        {
            await SetSectionAndOrganizationAsync(_context, _subscriptionService, section);
            ViewData["ViewTable"] = viewTable;

            switch (section.ToLower())
            {
                case "dashboard":
                    await LoadDashboardData();
                    break;
                case "security":
                    await LoadSecurityData();
                    break;
                case "etl":
                    await LoadETLData();
                    break;
                case "warehouse":
                    await LoadWarehouseData(viewTable);
                    break;
                case "cleansing":
                    await LoadCleansingData();
                    break;
                case "history":
                    await LoadHistoryData();
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

        private async Task LoadDashboardData()
        {
            var orgId = GetCurrentOrgId();

            // Consolidate user counts
            var userCounts = await _context.Users.AsNoTracking()
                .Where(u => u.OrganizationID == orgId)
                .GroupBy(u => u.Account_status == StatusActive)
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
                .GroupBy(s => s.Status == StatusActive)
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
            ViewData["PendingSubmissions"] = submissionCounts.Where(s => s.Status == StatusSubmitted).Sum(s => s.Count);
            ViewData["IntegratedSubmissions"] = submissionCounts.Where(s => s.Status == "Integrated").Sum(s => s.Count);
            ViewData["FailedSubmissions"] = submissionCounts.Where(s => s.Status == "Failed").Sum(s => s.Count);

            // Warehouse metrics
            ViewData["TotalWarehouseRows"] = await _context.WarehouseRecords.AsNoTracking().CountAsync(w => w.OrganizationID == orgId && w.RecordStatus == StatusActive);

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

        private async Task LoadSecurityData()
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
            var activeUsers = users.Count(u => u.Account_status == StatusActive);
            var inactiveUsers = users.Count(u => u.Account_status != StatusActive);
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

        private async Task LoadETLData()
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
            var batchSummaries = await _context.StagingRecords
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
                    ValidRows = g.Count(r => r.ValidationStatus == StatusValid),
                    ErrorRows = g.Count(r => r.ValidationStatus == StatusError),
                    PendingRows = g.Count(r => r.ValidationStatus == StatusPending),
                    PulledAt = g.Min(r => r.Pulled_at),
                    HasSubmitted = g.Any(r => r.SubmissionID != null),
                    AllValid = g.All(r => r.ValidationStatus == StatusValid),
                    HasPending = g.Any(r => r.ValidationStatus == StatusPending)
                })
                .OrderByDescending(b => b.PulledAt)
                .Take(20)
                .ToListAsync();

            var batches = batchSummaries
                .Select(b => new
                {
                    b.BatchId,
                    b.DataSourceName,
                    b.TargetTable,
                    b.TotalRows,
                    b.ValidRows,
                    b.ErrorRows,
                    b.PendingRows,
                    b.PulledAt,
                    Status = GetBatchStatus(b.HasSubmitted, b.AllValid, b.HasPending)
                })
                .ToList();

            ViewData["AllBatches"] = batches;

            var totalSources = sources.Count;
            var activeSources = sources.Count(s => s.Status == StatusActive);
            var totalSubmissions = submissions.Count;
            var integratedCount = submissions.Count(s => s.Status == "Integrated");

            ViewData["TotalSources"] = totalSources;
            ViewData["ActiveSources"] = activeSources;
            ViewData["TotalSubmissions"] = totalSubmissions;
            ViewData["IntegratedCount"] = integratedCount;
        }

        private async Task LoadWarehouseData(string? viewTable)
        {
            var orgId = GetCurrentOrgId();

            // Warehouse table summary
            var warehouseSummary = await _warehouseSummaryService.GetSummaryAsync(orgId, includeStatus: false, includeBatchCount: false);
            ViewData["WarehouseSummary"] = warehouseSummary;

            ViewData["TotalTables"] = warehouseSummary.Select(s => s.TargetTable).Distinct().Count();
            ViewData["TotalWarehouseRows"] = warehouseSummary.Sum(s => s.TotalRows);

            // Table names for dropdown
            var tableNames = warehouseSummary.Select(s => s.TargetTable).Distinct().ToList();
            ViewData["TableNames"] = tableNames;

            // If a specific table is selected, load its data rows
            if (TryGetSelectedTable(viewTable, tableNames, out var selectedTable))
            {
                var tableData = await _warehouseTableService.GetWarehouseTableDataAsync(orgId, selectedTable);
                ViewData["WarehouseColumns"] = tableData.Columns;
                ViewData["WarehouseRows"] = tableData.Rows;
                ViewData["WarehouseRowCount"] = tableData.RowCount;
                ViewData["SelectedTable"] = selectedTable;
            }

            // Source field mapping overview
            await LoadFieldMappingsAsync(orgId);
        }

        private async Task<List<WarehouseSummaryItem>> GetWarehouseSummaryAsync(int orgId)
        {
            return await _context.WarehouseRecords
                .AsNoTracking()
                .Include(w => w.DataSource)
                .Where(w => w.OrganizationID == orgId && w.RecordStatus == StatusActive)
                .GroupBy(w => new { w.TargetTable, w.DataSourceID })
                .Select(g => new WarehouseSummaryItem(
                    g.First().DataSource!.SourceName,
                    g.Key.TargetTable,
                    g.Max(w => w.Loaded_at),
                    g.Count(),
                    g.Max(w => w.Version)))
                .OrderByDescending(s => s.TotalRows)
                .ToListAsync();
        }

        private async Task LoadFieldMappingsAsync(int orgId)
        {
            var fieldMappings = await _context.DataSources
                .AsNoTracking()
                .Where(s => s.OrganizationID == orgId && s.Status == StatusActive)
                .Select(s => new { s.SourceName, s.TargetTable, s.ApiBaseUrl, s.ApiEndpoint })
                .ToListAsync();
            ViewData["FieldMappings"] = fieldMappings;
        }

        private static bool TryGetSelectedTable(string? viewTable, IReadOnlyCollection<string> tableNames, out string selectedTable)
        {
            if (!string.IsNullOrWhiteSpace(viewTable) && tableNames.Contains(viewTable))
            {
                selectedTable = viewTable;
                return true;
            }

            selectedTable = string.Empty;
            return false;
        }

        private async Task LoadWarehouseTableData(int orgId, string viewTable)
        {
            var warehouseRows = await _context.WarehouseRecords
                .AsNoTracking()
                .Where(w => w.OrganizationID == orgId && w.TargetTable == viewTable && w.RecordStatus == StatusActive)
                .OrderByDescending(w => w.Loaded_at)
                .ThenBy(w => w.RowNumber)
                .Take(100)
                .ToListAsync();

            var allColumns = new List<string>();
            var parsedRows = WarehouseRowParser.ParseWarehouseRows(warehouseRows, allColumns, _logger);

            ViewData["WarehouseColumns"] = allColumns;
            ViewData["WarehouseRows"] = parsedRows;
            ViewData["WarehouseRowCount"] = warehouseRows.Count;
            ViewData["SelectedTable"] = viewTable;
        }

        private sealed record WarehouseSummaryItem(
            string SourceName,
            string TargetTable,
            DateTime LastLoaded,
            int TotalRows,
            int LatestVersion);

        private async Task LoadCleansingData()
        {
            var orgId = GetCurrentOrgId();

            // Consolidate staging validation stats
            var cleansingStats = await _context.StagingRecords.AsNoTracking()
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalRows = cleansingStats.Sum(s => s.Count);
            var validRows = cleansingStats.Where(s => s.Status == StatusValid).Sum(s => s.Count);
            var errorRows = cleansingStats.Where(s => s.Status == StatusError).Sum(s => s.Count);
            var warningRows = cleansingStats.Where(s => s.Status == StatusWarning).Sum(s => s.Count);
            var pendingRows = cleansingStats.Where(s => s.Status == StatusPending).Sum(s => s.Count);

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
                    ValidRows = g.Count(r => r.ValidationStatus == StatusValid),
                    ErrorRows = g.Count(r => r.ValidationStatus == StatusError),
                    WarningRows = g.Count(r => r.ValidationStatus == StatusWarning),
                    PendingRows = g.Count(r => r.ValidationStatus == StatusPending),
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
                    ValidRows = g.Count(r => r.ValidationStatus == StatusValid),
                    ErrorRows = g.Count(r => r.ValidationStatus == StatusError),
                    WarningRows = g.Count(r => r.ValidationStatus == StatusWarning)
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
                    ActiveRecords = g.Count(w => w.RecordStatus == StatusActive),
                    ArchivedRecords = g.Count(w => w.RecordStatus == StatusArchived)
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
    }
}
