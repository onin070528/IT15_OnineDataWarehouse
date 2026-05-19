using it15_webproject_mvc.Data;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Services
{
    public class SubscriptionService
    {
        private const string PaymentStatusPaid = "Paid";
        private const string SubscriptionFree = "Free";
        private const string SubscriptionBasic = "Basic";
        private const string SubscriptionPremium = "Premium";

        private readonly ApplicationDbContext _context;

        public SubscriptionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<string> EnsureCurrentPlanAsync(int orgId, string currentPlan)
        {
            if (orgId <= 0)
            {
                return currentPlan;
            }

            var org = await _context.Organizations.FindAsync(orgId);
            if (org == null)
            {
                return currentPlan;
            }

            var latestPayment = await _context.Payments
                .AsNoTracking()
                .Where(p => p.OrganizationID == orgId && p.Status == PaymentStatusPaid && p.Paid_at.HasValue)
                .OrderByDescending(p => p.Paid_at)
                .FirstOrDefaultAsync();

            var now = DateTime.UtcNow;
            var newPlan = SubscriptionFree;

            if (latestPayment != null)
            {
                var paidAt = latestPayment.Paid_at!.Value;
                if (paidAt.AddMonths(1) > now)
                {
                    newPlan = latestPayment.PlanName.Contains(SubscriptionPremium, StringComparison.OrdinalIgnoreCase)
                        ? SubscriptionPremium
                        : SubscriptionBasic;
                }
            }

            if (!string.Equals(org.SubscriptionPlan, newPlan, StringComparison.Ordinal))
            {
                org.SubscriptionPlan = newPlan;
                await _context.SaveChangesAsync();
            }

            return org.SubscriptionPlan;
        }
    }
}
