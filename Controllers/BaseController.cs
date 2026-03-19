using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace it15_webproject_mvc.Controllers
{
    public abstract class BaseController : Controller
    {
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
