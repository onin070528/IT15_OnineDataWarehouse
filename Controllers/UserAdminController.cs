using System.Security.Claims;
using System.Text.Json;
using it15_webproject_mvc.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Controllers
{
    [Authorize(Roles = "UserAdmin")]
    public class UserAdminController : BaseController
    {
        private readonly ApplicationDbContext _context;

        public UserAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> UserNav(string section = "dashboard", string? viewTable = null)
        {
            ViewData["Section"] = section.ToLower();
            ViewData["ViewTable"] = viewTable;
            ViewData["OrganizationName"] = GetCurrentOrgName();
            var subPlan = GetCurrentSubscriptionPlan();
            ViewData["SubscriptionPlan"] = subPlan;

            var adminUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (adminUserIdClaim != null)
            {
                var adminUserId = int.Parse(adminUserIdClaim);
                var adminUser = await _context.Users.Include(u => u.Organization).FirstOrDefaultAsync(u => u.UserID == adminUserId);
                if (adminUser != null)
                {
                    var orgId = adminUser.OrganizationID;
                    // Always use the DB value as source of truth
                    subPlan = adminUser.Organization?.SubscriptionPlan ?? "Free";
                    ViewData["SubscriptionPlan"] = subPlan;

                    var orgUsers = await _context.Users
                        .AsNoTracking()
                        .Include(u => u.Role)
                        .Include(u => u.Organization)
                        .Where(u => u.OrganizationID == orgId)
                        .OrderByDescending(u => u.Created_at)
                        .ToListAsync();
                    ViewData["OrgUsers"] = orgUsers;

                    switch (section.ToLower())
                    {
                        case "dashboard":
                            await LoadDashboardData(orgId);
                            break;
                        case "subscription":
                            var payments = await _context.Payments
                                .AsNoTracking()
                                .Where(p => p.OrganizationID == orgId)
                                .OrderByDescending(p => p.Created_at)
                                .ToListAsync();
                            ViewData["Payments"] = payments;
                            break;
                        case "datasources":
                            await LoadDataSourcesData(orgId);
                            break;
                        case "etl":
                            await LoadETLData(orgId);
                            break;
                        case "storage":
                            await LoadStorageData(orgId, viewTable);
                            break;
                        case "cleansing":
                            await LoadCleansingData(orgId);
                            break;
                        case "logs":
                            await LoadHistoryData(orgId);
                            break;
                    }
                }
            }

            return View();
        }

        public IActionResult UserIndex() => View();
        public IActionResult UserSide() => View();
        public IActionResult UserDataSources() => View();
        public IActionResult UserETL() => View();
        public IActionResult UserStorage() => View();
        public IActionResult UserCleansing() => View();
        public IActionResult UserHistory() => View();

        // === TOGGLE DATA SOURCE STATUS ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSourceStatus(int dataSourceId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var adminUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (adminUserIdClaim == null) return RedirectToAction("Login", "Home");

            var adminUserId = int.Parse(adminUserIdClaim);
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.UserID == adminUserId);
            if (adminUser == null) return RedirectToAction("Login", "Home");

            var source = await _context.DataSources.FirstOrDefaultAsync(s => s.DataSourceID == dataSourceId && s.OrganizationID == adminUser.OrganizationID);
            if (source == null) return NotFound();

            var oldStatus = source.Status;
            source.Status = source.Status == "Active" ? "Inactive" : "Active";

            _context.AuditLogs.Add(new Models.AuditLog
            {
                Action = "Source Status Changed",
                EntityType = "DataSource",
                EntityId = source.DataSourceID,
                EntityName = source.SourceName,
                Details = $"Status changed from {oldStatus} to {source.Status}",
                PerformedByUserID = adminUserId,
                OrganizationID = adminUser.OrganizationID,
                Performed_at = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Source '{source.SourceName}' is now {source.Status}.";
            return RedirectToAction("UserNav", new { section = "datasources" });
        }

        // === ARCHIVE WAREHOUSE TABLE ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveTable(string targetTable)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var adminUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (adminUserIdClaim == null) return RedirectToAction("Login", "Home");

            var adminUserId = int.Parse(adminUserIdClaim);
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.UserID == adminUserId);
            if (adminUser == null) return RedirectToAction("Login", "Home");

            var orgId = adminUser.OrganizationID;
            var records = await _context.WarehouseRecords
                .Where(w => w.OrganizationID == orgId && w.TargetTable == targetTable && w.RecordStatus == "Active")
                .ToListAsync();

            if (!records.Any())
            {
                TempData["Error"] = "No active records found for that table.";
                return RedirectToAction("UserNav", new { section = "storage" });
            }

            foreach (var r in records)
            {
                r.RecordStatus = "Archived";
            }

            _context.AuditLogs.Add(new Models.AuditLog
            {
                Action = "Table Archived",
                EntityType = "WarehouseRecord",
                EntityName = targetTable,
                Details = $"Archived {records.Count} active records from table '{targetTable}'",
                PerformedByUserID = adminUserId,
                OrganizationID = orgId,
                Performed_at = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Archived {records.Count} records from table '{targetTable}'.";
            return RedirectToAction("UserNav", new { section = "storage" });
        }

        // === RESTORE ARCHIVED TABLE ===

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreTable(string targetTable)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var adminUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (adminUserIdClaim == null) return RedirectToAction("Login", "Home");

            var adminUserId = int.Parse(adminUserIdClaim);
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.UserID == adminUserId);
            if (adminUser == null) return RedirectToAction("Login", "Home");

            var orgId = adminUser.OrganizationID;
            var records = await _context.WarehouseRecords
                .Where(w => w.OrganizationID == orgId && w.TargetTable == targetTable && w.RecordStatus == "Archived")
                .ToListAsync();

            if (!records.Any())
            {
                TempData["Error"] = "No archived records found for that table.";
                return RedirectToAction("UserNav", new { section = "storage" });
            }

            foreach (var r in records)
            {
                r.RecordStatus = "Active";
            }

            _context.AuditLogs.Add(new Models.AuditLog
            {
                Action = "Table Restored",
                EntityType = "WarehouseRecord",
                EntityName = targetTable,
                Details = $"Restored {records.Count} archived records for table '{targetTable}'",
                PerformedByUserID = adminUserId,
                OrganizationID = orgId,
                Performed_at = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Restored {records.Count} records for table '{targetTable}'.";
            return RedirectToAction("UserNav", new { section = "storage" });
        }

        private async Task LoadDashboardData(int orgId)
        {
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
            ViewData["IntegratedCount"] = submissionCounts.Where(s => s.Status == "Integrated").Sum(s => s.Count);
            ViewData["PendingCount"] = submissionCounts.Where(s => s.Status == "Submitted").Sum(s => s.Count);
            ViewData["FailedCount"] = submissionCounts.Where(s => s.Status == "Failed").Sum(s => s.Count);

            // Consolidate staging record counts
            var stagingCounts = await _context.StagingRecords.AsNoTracking()
                .Where(r => r.DataSource!.OrganizationID == orgId)
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalStagingRows"] = stagingCounts.Sum(s => s.Count);
            ViewData["ValidRows"] = stagingCounts.Where(s => s.Status == "Valid").Sum(s => s.Count);
            ViewData["ErrorRows"] = stagingCounts.Where(s => s.Status == "Error").Sum(s => s.Count);

            ViewData["TotalAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);

            var recentLogs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .OrderByDescending(a => a.Performed_at)
                .Take(5)
                .ToListAsync();
            ViewData["RecentLogs"] = recentLogs;

            var recentSubmissions = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(5)
                .ToListAsync();
            ViewData["RecentSubmissions"] = recentSubmissions;
        }

        private async Task LoadDataSourcesData(int orgId)
        {
            var sources = await _context.DataSources
                .AsNoTracking()
                .Include(s => s.CreatedByUser)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .ToListAsync();
            ViewData["AllSources"] = sources;

            ViewData["TotalSources"] = sources.Count;
            ViewData["ActiveSources"] = sources.Count(s => s.Status == "Active");
        }

        private async Task LoadETLData(int orgId)
        {
            var submissions = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Include(s => s.SubmittedByUser)
                .Where(s => s.OrganizationID == orgId)
                .OrderByDescending(s => s.Created_at)
                .Take(50)
                .ToListAsync();
            ViewData["AllSubmissions"] = submissions;

            // Consolidate submission counts into single query
            var etlCounts = await _context.DataSubmissions.AsNoTracking()
                .Where(s => s.OrganizationID == orgId)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Rows = g.Sum(s => s.TotalRows) })
                .ToListAsync();
            ViewData["TotalSubmissions"] = etlCounts.Sum(s => s.Count);
            ViewData["IntegratedCount"] = etlCounts.Where(s => s.Status == "Integrated").Sum(s => s.Count);
            ViewData["PendingCount"] = etlCounts.Where(s => s.Status == "Submitted").Sum(s => s.Count);
            ViewData["FailedCount"] = etlCounts.Where(s => s.Status == "Failed").Sum(s => s.Count);
            ViewData["TotalRowsProcessed"] = etlCounts.Sum(s => s.Rows);
        }

        private async Task LoadStorageData(int orgId, string? viewTable)
        {
            // Warehouse records grouped by target table
            var warehouseSummary = await _context.WarehouseRecords
                .AsNoTracking()
                .Include(w => w.DataSource)
                .Where(w => w.OrganizationID == orgId && w.RecordStatus == "Active")
                .GroupBy(w => new { w.TargetTable, w.DataSourceID })
                .Select(g => new
                {
                    SourceName = g.First().DataSource!.SourceName,
                    TargetTable = g.Key.TargetTable,
                    Status = g.First().DataSource!.Status,
                    LastLoaded = g.Max(w => w.Loaded_at),
                    BatchCount = g.Select(w => w.BatchId).Distinct().Count(),
                    TotalRows = g.Count(),
                    LatestVersion = g.Max(w => w.Version)
                })
                .OrderByDescending(s => s.TotalRows)
                .ToListAsync();
            ViewData["WarehouseSummary"] = warehouseSummary;

            ViewData["TotalTables"] = warehouseSummary.Select(s => s.TargetTable).Distinct().Count();
            ViewData["TotalWarehouseRows"] = warehouseSummary.Sum(s => s.TotalRows);
            ViewData["TotalBatches"] = warehouseSummary.Sum(s => s.BatchCount);

            // Also load recent warehouse load events
            var recentLoads = await _context.DataSubmissions
                .AsNoTracking()
                .Include(s => s.DataSource)
                .Where(s => s.OrganizationID == orgId && s.Status == "Integrated")
                .OrderByDescending(s => s.Integrated_at)
                .Take(10)
                .ToListAsync();
            ViewData["RecentLoads"] = recentLoads;

            // Staging vs Warehouse comparison
            var totalStagingRows = await _context.StagingRecords.AsNoTracking().CountAsync(r => r.DataSource!.OrganizationID == orgId);
            var totalWarehouseRows = await _context.WarehouseRecords.AsNoTracking().CountAsync(w => w.OrganizationID == orgId && w.RecordStatus == "Active");
            var archivedRows = await _context.WarehouseRecords.AsNoTracking().CountAsync(w => w.OrganizationID == orgId && w.RecordStatus == "Archived");
            ViewData["TotalStagingRows"] = totalStagingRows;
            ViewData["TotalStoredRows"] = totalWarehouseRows;
            ViewData["ArchivedRows"] = archivedRows;

            // Load available table names for the dropdown
            var tableNames = warehouseSummary.Select(s => (string)s.TargetTable).Distinct().ToList();
            ViewData["TableNames"] = tableNames;

            // Archived tables for restore actions (includes tables that currently have no active rows)
            var archivedTableNames = await _context.WarehouseRecords
                .AsNoTracking()
                .Where(w => w.OrganizationID == orgId && w.RecordStatus == "Archived")
                .Select(w => w.TargetTable)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();
            ViewData["ArchivedTableNames"] = archivedTableNames;

            // If a specific table is selected, load its actual data rows
            if (!string.IsNullOrEmpty(viewTable) && tableNames.Contains(viewTable))
            {
                var warehouseRows = await _context.WarehouseRecords
                    .AsNoTracking()
                    .Where(w => w.OrganizationID == orgId && w.TargetTable == viewTable && w.RecordStatus == "Active")
                    .OrderByDescending(w => w.Loaded_at)
                    .ThenBy(w => w.RowNumber)
                    .Take(100)
                    .ToListAsync();

                // Parse the CleanData JSON to extract column names and row values
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
        }

        private async Task LoadCleansingData(int orgId)
        {
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

            // Consolidate staging record counts into single query
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
        }

        private async Task LoadHistoryData(int orgId)
        {
            // Log limit: Free=20, Basic=50, Premium=100
            var org = await _context.Organizations.FindAsync(orgId);
            var plan = org?.SubscriptionPlan ?? "Free";
            var logLimit = plan switch { "Premium" => 100, "Basic" => 50, _ => 20 };

            var logs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .Where(a => a.OrganizationID == orgId)
                .OrderByDescending(a => a.Performed_at)
                .Take(logLimit)
                .ToListAsync();
            ViewData["AuditLogs"] = logs;

            ViewData["TotalActions"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId);
            ViewData["TodayActions"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.OrganizationID == orgId && a.Performed_at.Date == DateTime.UtcNow.Date);
            ViewData["UniqueEntities"] = await _context.AuditLogs.AsNoTracking().Where(a => a.OrganizationID == orgId).Select(a => a.EntityName).Distinct().CountAsync();
            ViewData["UniqueUsers"] = await _context.AuditLogs.AsNoTracking().Where(a => a.OrganizationID == orgId).Select(a => a.PerformedByUserID).Distinct().CountAsync();
            ViewData["LogLimit"] = logLimit;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(string fullName, string username, string email, string role, string password, string confirmPassword, string accountStatus)
        {
            var section = "users";

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword) || string.IsNullOrWhiteSpace(role))
            {
                TempData["Error"] = "All fields are required.";
                return RedirectToAction("UserNav", new { section });
            }

            if (password != confirmPassword)
            {
                TempData["Error"] = "Passwords do not match.";
                return RedirectToAction("UserNav", new { section });
            }

            // Validate password strength: minimum 12 characters and at least one special character
            if (password.Length < 12 || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                TempData["Error"] = "Password must be at least 12 characters and contain at least one special character.";
                return RedirectToAction("UserNav", new { section });
            }

            if (await _context.Users.AnyAsync(u => u.Username == username))
            {
                TempData["Error"] = "Username is already taken.";
                return RedirectToAction("UserNav", new { section });
            }

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                TempData["Error"] = "Email is already registered.";
                return RedirectToAction("UserNav", new { section });
            }

            var assignedRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == role);
            if (assignedRole == null)
            {
                TempData["Error"] = "Invalid role selected.";
                return RedirectToAction("UserNav", new { section });
            }

            var adminUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (adminUserIdClaim == null)
            {
                return RedirectToAction("Login", "Home");
            }

            var adminUser = await _context.Users.Include(u => u.Organization).FirstOrDefaultAsync(u => u.UserID == int.Parse(adminUserIdClaim));
            if (adminUser == null)
            {
                return RedirectToAction("Login", "Home");
            }

            // Enforce subscription limits
            var orgPlan = adminUser.Organization?.SubscriptionPlan ?? "Free";
            var currentUserCount = await _context.Users.CountAsync(u => u.OrganizationID == adminUser.OrganizationID);

            // User limit: Free=3, Basic=10, Premium=Unlimited
            var maxUsers = orgPlan switch { "Premium" => int.MaxValue, "Basic" => 10, _ => 3 };
            if (currentUserCount >= maxUsers)
            {
                TempData["Error"] = $"User limit reached for your {orgPlan} plan ({maxUsers} users max). Please upgrade your subscription.";
                return RedirectToAction("UserNav", new { section });
            }

            // Role restrictions: Free=Staff only, Basic=Staff+Manager, Premium=All
            var allowedRoles = orgPlan switch
            {
                "Premium" => new[] { "Staff", "Manager", "DataAnalyst" },
                "Basic" => new[] { "Staff", "Manager" },
                _ => new[] { "Staff" }
            };
            if (!allowedRoles.Contains(role))
            {
                TempData["Error"] = $"The '{role}' role is not available on your {orgPlan} plan. Please upgrade your subscription.";
                return RedirectToAction("UserNav", new { section });
            }

            var newUser = new Models.User
            {
                OrganizationID = adminUser.OrganizationID,
                RoleID = assignedRole.RoleID,
                Username = username.Trim(),
                Full_name = fullName.Trim(),
                Email = email.Trim(),
                Password = BCrypt.Net.BCrypt.HashPassword(password),
                Account_status = accountStatus ?? "Active",
                Created_at = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"User '{fullName}' has been created successfully.";
            return RedirectToAction("UserNav", new { section });
        }
    }
}
