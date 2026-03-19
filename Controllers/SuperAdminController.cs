using System.Security.Claims;
using it15_webproject_mvc.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : BaseController
    {
        private readonly ApplicationDbContext _context;

        public SuperAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Supernav(string section = "dashboard")
        {
            ViewData["Section"] = section.ToLower();
            ViewData["OrganizationName"] = GetCurrentOrgName();

            switch (section.ToLower())
            {
                case "dashboard":
                    await LoadDashboardData();
                    break;
                case "reports":
                    await LoadReportsData();
                    break;
                case "subscriptions":
                    await LoadSubscriptionsData();
                    break;
                case "sales":
                    await LoadSalesData();
                    break;
                case "users":
                    await LoadUsersData();
                    break;
            }

            return View();
        }

        public IActionResult SuperDashboard() => View();
        public IActionResult SuperReports() => View();
        public IActionResult SuperSubs() => View();
        public IActionResult SuperSales() => View();
        public IActionResult SuperUsers() => View();

        private async Task LoadDashboardData()
        {
            ViewData["TotalOrganizations"] = await _context.Organizations.AsNoTracking().CountAsync();

            // Consolidate user counts
            var userCounts = await _context.Users.AsNoTracking()
                .GroupBy(u => u.Account_status == "Active")
                .Select(g => new { IsActive = g.Key, Count = g.Count() })
                .ToListAsync();
            var totalUsers = userCounts.Sum(u => u.Count);
            ViewData["TotalUsers"] = totalUsers;
            ViewData["ActiveUsers"] = userCounts.Where(u => u.IsActive).Sum(u => u.Count);
            ViewData["InactiveUsers"] = userCounts.Where(u => !u.IsActive).Sum(u => u.Count);

            ViewData["TotalRoles"] = await _context.Roles.AsNoTracking().CountAsync();

            // Consolidate source counts
            var sourceCounts = await _context.DataSources.AsNoTracking()
                .GroupBy(s => s.Status == "Active")
                .Select(g => new { IsActive = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalSources"] = sourceCounts.Sum(s => s.Count);
            ViewData["ActiveSources"] = sourceCounts.Where(s => s.IsActive).Sum(s => s.Count);

            // Consolidate submission counts
            var submissionCounts = await _context.DataSubmissions.AsNoTracking()
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalSubmissions"] = submissionCounts.Sum(s => s.Count);
            ViewData["PendingSubmissions"] = submissionCounts.Where(s => s.Status == "Submitted").Sum(s => s.Count);
            ViewData["IntegratedSubmissions"] = submissionCounts.Where(s => s.Status == "Integrated").Sum(s => s.Count);
            ViewData["FailedSubmissions"] = submissionCounts.Where(s => s.Status == "Failed").Sum(s => s.Count);

            ViewData["TotalAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync();
            ViewData["TodayAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync(a => a.Performed_at.Date == DateTime.UtcNow.Date);

            // Consolidate staging record counts
            var stagingCounts = await _context.StagingRecords.AsNoTracking()
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["TotalStagingRows"] = stagingCounts.Sum(s => s.Count);
            ViewData["ValidRows"] = stagingCounts.Where(s => s.Status == "Valid").Sum(s => s.Count);
            ViewData["ErrorRows"] = stagingCounts.Where(s => s.Status == "Error").Sum(s => s.Count);

            // Income KPIs
            ViewData["TotalRevenue"] = await _context.Payments.AsNoTracking().Where(p => p.Status == "Paid").SumAsync(p => p.Amount);
            ViewData["ThisMonthRevenue"] = await _context.Payments.AsNoTracking()
                .Where(p => p.Status == "Paid" && p.Paid_at != null && p.Paid_at.Value.Month == DateTime.UtcNow.Month && p.Paid_at.Value.Year == DateTime.UtcNow.Year)
                .SumAsync(p => p.Amount);
            ViewData["PaidSubscriptions"] = await _context.Payments.AsNoTracking().CountAsync(p => p.Status == "Paid");

            // Consolidate org plan counts
            var orgPlanCounts = await _context.Organizations.AsNoTracking()
                .GroupBy(o => o.SubscriptionPlan)
                .Select(g => new { Plan = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewData["FreeOrgs"] = orgPlanCounts.Where(o => o.Plan == "Free").Sum(o => o.Count);
            ViewData["BasicOrgs"] = orgPlanCounts.Where(o => o.Plan == "Basic").Sum(o => o.Count);
            ViewData["PremiumOrgs"] = orgPlanCounts.Where(o => o.Plan == "Premium").Sum(o => o.Count);

            var recentPayments = await _context.Payments
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Organization)
                .Where(p => p.Status == "Paid")
                .OrderByDescending(p => p.Paid_at)
                .Take(5)
                .ToListAsync();
            ViewData["RecentPayments"] = recentPayments;

            var recentLogs = await _context.AuditLogs
                .AsNoTracking()
                .Include(a => a.PerformedByUser)
                .OrderByDescending(a => a.Performed_at)
                .Take(8)
                .ToListAsync();
            ViewData["RecentLogs"] = recentLogs;

            var recentUsers = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Include(u => u.Organization)
                .OrderByDescending(u => u.Created_at)
                .Take(5)
                .ToListAsync();
            ViewData["RecentUsers"] = recentUsers;
        }

        private async Task LoadReportsData()
        {
            // === Platform KPIs ===
            ViewData["TotalOrganizations"] = await _context.Organizations.AsNoTracking().CountAsync();
            ViewData["TotalUsers"] = await _context.Users.AsNoTracking().CountAsync();
            ViewData["TotalSources"] = await _context.DataSources.AsNoTracking().CountAsync();
            ViewData["TotalSubmissions"] = await _context.DataSubmissions.AsNoTracking().CountAsync();
            ViewData["TotalAuditLogs"] = await _context.AuditLogs.AsNoTracking().CountAsync();
            ViewData["TotalWarehouseRows"] = await _context.WarehouseRecords.AsNoTracking().CountAsync();

            // === Subscription Plan Distribution ===
            var planDistribution = await _context.Organizations.AsNoTracking()
                .GroupBy(o => o.SubscriptionPlan)
                .Select(g => new { Plan = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();
            ViewData["PlanDistribution"] = planDistribution;

            // === Organization Activity Ranking ===
            var orgActivity = await _context.Organizations.AsNoTracking()
                .Select(o => new
                {
                    o.OrganizationName,
                    o.SubscriptionPlan,
                    UserCount = _context.Users.Count(u => u.OrganizationID == o.OrganizationID),
                    SourceCount = _context.DataSources.Count(d => d.OrganizationID == o.OrganizationID),
                    SubmissionCount = _context.DataSubmissions.Count(s => s.OrganizationID == o.OrganizationID),
                    WarehouseRows = _context.WarehouseRecords.Count(w => w.OrganizationID == o.OrganizationID),
                    o.Created_at
                })
                .OrderByDescending(o => o.SubmissionCount)
                .ToListAsync();
            ViewData["OrgActivity"] = orgActivity;

            // === Submission Status Platform-wide ===
            var submissionStatus = await _context.DataSubmissions.AsNoTracking()
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Rows = g.Sum(s => s.TotalRows) })
                .ToListAsync();
            ViewData["SubmissionStatus"] = submissionStatus;

            // === Monthly Submission Trend (last 12 months) ===
            var monthlySubmissions = await _context.DataSubmissions.AsNoTracking()
                .GroupBy(s => new { s.Created_at.Year, s.Created_at.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Count = g.Count(), Rows = g.Sum(s => s.TotalRows) })
                .OrderByDescending(g => g.Year).ThenByDescending(g => g.Month)
                .Take(12)
                .ToListAsync();
            ViewData["MonthlySubmissions"] = monthlySubmissions;

            // === User Growth (last 12 months) ===
            var userGrowth = await _context.Users.AsNoTracking()
                .GroupBy(u => new { u.Created_at.Year, u.Created_at.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Count = g.Count() })
                .OrderByDescending(g => g.Year).ThenByDescending(g => g.Month)
                .Take(12)
                .ToListAsync();
            ViewData["UserGrowth"] = userGrowth;

            // === System Error Rate ===
            var stagingCounts = await _context.StagingRecords.AsNoTracking()
                .GroupBy(r => r.ValidationStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var totalStaging = stagingCounts.Sum(s => s.Count);
            var validRows = stagingCounts.Where(s => s.Status == "Valid").Sum(s => s.Count);
            var errorRows = stagingCounts.Where(s => s.Status == "Error").Sum(s => s.Count);
            ViewData["TotalStagingRows"] = totalStaging;
            ViewData["ValidRows"] = validRows;
            ViewData["ErrorRows"] = errorRows;
            ViewData["DataQuality"] = totalStaging > 0 ? Math.Round(validRows * 100.0 / totalStaging, 1) : 0.0;

            // === Revenue Summary ===
            ViewData["TotalRevenue"] = await _context.Payments.AsNoTracking().Where(p => p.Status == "Paid").SumAsync(p => p.Amount);
            ViewData["ThisMonthRevenue"] = await _context.Payments.AsNoTracking()
                .Where(p => p.Status == "Paid" && p.Paid_at != null && p.Paid_at.Value.Month == DateTime.UtcNow.Month && p.Paid_at.Value.Year == DateTime.UtcNow.Year)
                .SumAsync(p => p.Amount);

            // === Audit Action Breakdown ===
            var actionBreakdown = await _context.AuditLogs.AsNoTracking()
                .GroupBy(a => a.Action)
                .Select(g => new { Action = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();
            ViewData["ActionBreakdown"] = actionBreakdown;

            // === Failed Submissions by Org ===
            var failedByOrg = await _context.DataSubmissions.AsNoTracking()
                .Where(s => s.Status == "Failed")
                .GroupBy(s => s.Organization!.OrganizationName)
                .Select(g => new { OrgName = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToListAsync();
            ViewData["FailedByOrg"] = failedByOrg;
        }

        private async Task LoadSubscriptionsData()
        {
            var organizations = await _context.Organizations
                .AsNoTracking()
                .Include(o => o.Users)
                .ThenInclude(u => u.Role)
                .OrderBy(o => o.OrganizationName)
                .ToListAsync();
            ViewData["Organizations"] = organizations;

            ViewData["TotalOrganizations"] = organizations.Count;
            ViewData["TotalUsersAll"] = organizations.Sum(o => o.Users.Count);
            ViewData["FreeOrgs"] = organizations.Count(o => o.SubscriptionPlan == "Free");
            ViewData["BasicOrgs"] = organizations.Count(o => o.SubscriptionPlan == "Basic");
            ViewData["PremiumOrgs"] = organizations.Count(o => o.SubscriptionPlan == "Premium");

            // Load all payments with user + org info
            var allPayments = await _context.Payments
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Organization)
                .OrderByDescending(p => p.Created_at)
                .ToListAsync();
            ViewData["AllPayments"] = allPayments;

            ViewData["TotalRevenue"] = allPayments.Where(p => p.Status == "Paid").Sum(p => p.Amount);
            ViewData["PendingPayments"] = allPayments.Count(p => p.Status == "Pending");
            ViewData["PaidPayments"] = allPayments.Count(p => p.Status == "Paid");

            var roleDistribution = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .GroupBy(u => u.Role!.RoleName)
                .Select(g => new { Role = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();
            ViewData["RoleDistribution"] = roleDistribution;
        }

        private async Task LoadSalesData()
        {
            // All payments for income tracking
            var allPayments = await _context.Payments
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Organization)
                .OrderByDescending(p => p.Created_at)
                .ToListAsync();
            ViewData["AllPayments"] = allPayments;

            var paidPayments = allPayments.Where(p => p.Status == "Paid").ToList();
            ViewData["TotalRevenue"] = paidPayments.Sum(p => p.Amount);
            ViewData["TotalPaidCount"] = paidPayments.Count;
            ViewData["PendingPayments"] = allPayments.Count(p => p.Status == "Pending");
            ViewData["TotalTransactions"] = allPayments.Count;

            // This month revenue
            var thisMonthPaid = paidPayments.Where(p => p.Paid_at != null && p.Paid_at.Value.Month == DateTime.UtcNow.Month && p.Paid_at.Value.Year == DateTime.UtcNow.Year).ToList();
            ViewData["ThisMonthRevenue"] = thisMonthPaid.Sum(p => p.Amount);
            ViewData["ThisMonthCount"] = thisMonthPaid.Count;

            // Monthly income breakdown (last 12 months)
            var monthlyIncome = paidPayments
                .Where(p => p.Paid_at != null)
                .GroupBy(p => new { p.Paid_at!.Value.Year, p.Paid_at!.Value.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Total = g.Sum(p => p.Amount), Count = g.Count() })
                .OrderByDescending(g => g.Year).ThenByDescending(g => g.Month)
                .Take(12)
                .ToList();
            ViewData["MonthlyIncome"] = monthlyIncome;

            // Revenue by plan
            var revenueByPlan = paidPayments
                .GroupBy(p => p.PlanName)
                .Select(g => new { Plan = g.Key, Total = g.Sum(p => p.Amount), Count = g.Count() })
                .OrderByDescending(g => g.Total)
                .ToList();
            ViewData["RevenueByPlan"] = revenueByPlan;

            // Revenue by organization
            var revenueByOrg = paidPayments
                .GroupBy(p => p.Organization?.OrganizationName ?? "Unknown")
                .Select(g => new { OrgName = g.Key, Total = g.Sum(p => p.Amount), Count = g.Count() })
                .OrderByDescending(g => g.Total)
                .ToList();
            ViewData["RevenueByOrg"] = revenueByOrg;
        }

        private async Task LoadUsersData()
        {
            var users = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Include(u => u.Organization)
                .OrderByDescending(u => u.Created_at)
                .ToListAsync();
            ViewData["AllUsers"] = users;

            ViewData["TotalUsers"] = users.Count;
            ViewData["ActiveUsers"] = users.Count(u => u.Account_status == "Active");
            ViewData["InactiveUsers"] = users.Count(u => u.Account_status != "Active");
            ViewData["TodayLogins"] = users.Count(u => u.Last_login.HasValue && u.Last_login.Value.Date == DateTime.UtcNow.Date);

            var roles = await _context.Roles.AsNoTracking().OrderBy(r => r.RoleName).ToListAsync();
            ViewData["AllRoles"] = roles;

            var organizations = await _context.Organizations.AsNoTracking().OrderBy(o => o.OrganizationName).ToListAsync();
            ViewData["AllOrganizations"] = organizations;
        }
    }
}
