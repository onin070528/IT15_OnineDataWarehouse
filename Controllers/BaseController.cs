using System.Security.Claims;
using System.Security.Claims;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Services;
using Microsoft.AspNetCore.Mvc;

namespace it15_webproject_mvc.Controllers
{
    public abstract class BaseController : Controller
    {
        protected async Task SetSectionAndOrganizationAsync(ApplicationDbContext context, SubscriptionService subscriptionService, string section)
        {
            ViewData["Section"] = section.ToLower();
            ViewData["OrganizationName"] = GetCurrentOrgName();

            var orgId = GetCurrentOrgId();
            var org = await context.Organizations.FindAsync(orgId);
            var currentPlan = org?.SubscriptionPlan ?? "Free";
            var updatedPlan = await subscriptionService.EnsureCurrentPlanAsync(orgId, currentPlan);
            ViewData["SubscriptionPlan"] = updatedPlan;
        }

        protected int GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null ? int.Parse(claim.Value) : 0;
        }

        protected int GetCurrentOrgId()
        {
            var claim = User.FindFirst("OrganizationID");
            return claim != null ? int.Parse(claim.Value) : 0;
        }

        protected string GetCurrentOrgName()
        {
            return User.FindFirst("OrganizationName")?.Value ?? "My Company";
        }

        protected string GetCurrentSubscriptionPlan()
        {
            return User.FindFirst("SubscriptionPlan")?.Value ?? "Free";
        }
    }
}
