using System.Security.Claims;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;
using it15_webproject_mvc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Controllers
{
    [Authorize]
    public class PaymentController : BaseController
    {
        private readonly ApplicationDbContext _context;
        private readonly PayMongoService _payMongoService;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(ApplicationDbContext context, PayMongoService payMongoService, ILogger<PaymentController> logger)
        {
            _context = context;
            _payMongoService = payMongoService;
            _logger = logger;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(string plan)
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
                return RedirectToAction("Login", "Home");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return RedirectToAction("Login", "Home");

            string planName;
            long amountInCentavos;
            string description;

            switch (plan?.ToLower())
            {
                case "basic":
                    planName = "Basic Plan";
                    amountInCentavos = 15000; // ?150.00
                    description = "Data Warehouse Basic Plan - Monthly Subscription";
                    break;
                case "premium":
                    planName = "Premium Plan";
                    amountInCentavos = 50000; // ?500.00
                    description = "Data Warehouse Premium Plan - Monthly Subscription";
                    break;
                default:
                    TempData["Error"] = "Invalid plan selected.";
                    return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var successUrl = $"{baseUrl}/Payment/Success";
            var cancelUrl = $"{baseUrl}/Payment/Cancel";

            var (checkoutUrl, sessionId, error) = await _payMongoService.CreateCheckoutSession(
                planName, amountInCentavos, "PHP", description, successUrl, cancelUrl);

            if (error != null || checkoutUrl == null)
            {
                _logger.LogError("Failed to create checkout: {Error}", error);
                TempData["Error"] = "Failed to initiate payment. Please try again.";
                return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
            }

            var payment = new Payment
            {
                UserID = userId,
                OrganizationID = user.OrganizationID,
                PlanName = planName,
                Amount = amountInCentavos / 100m,
                Currency = "PHP",
                Status = "Pending",
                CheckoutSessionId = sessionId,
                CheckoutUrl = checkoutUrl,
                Created_at = DateTime.UtcNow
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            return Redirect(checkoutUrl);
        }

        public async Task<IActionResult> Success(string session_id)
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
                return RedirectToAction("Login", "Home");

            Payment? payment = null;

            // Try to find payment by session_id if provided
            if (!string.IsNullOrEmpty(session_id))
            {
                payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.CheckoutSessionId == session_id);
            }

            // Fallback: find the most recent pending payment for the current user
            if (payment == null)
            {
                payment = await _context.Payments
                    .Where(p => p.UserID == userId && p.Status == "Pending")
                    .OrderByDescending(p => p.Created_at)
                    .FirstOrDefaultAsync();
            }

            if (payment == null)
            {
                TempData["Error"] = "Payment record not found.";
                return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
            }

            var checkoutSessionId = payment.CheckoutSessionId;
            if (string.IsNullOrEmpty(checkoutSessionId))
            {
                TempData["Error"] = "Invalid payment session.";
                return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
            }

            var (status, paymentId, error) = await _payMongoService.GetCheckoutSession(checkoutSessionId);

            if (error != null)
            {
                _logger.LogError("Failed to verify payment: {Error}", error);
                TempData["Error"] = "Failed to verify payment. Please contact support.";
                return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
            }

            if (status == "active" || status == "paid")
            {
                payment.Status = "Paid";
                payment.PayMongoPaymentId = paymentId;
                payment.Paid_at = DateTime.UtcNow;

                // Update organization subscription plan
                var org = await _context.Organizations.FindAsync(payment.OrganizationID);
                if (org != null)
                {
                    org.SubscriptionPlan = payment.PlanName.Contains("Premium") ? "Premium" : "Basic";
                }

                await _context.SaveChangesAsync();

                TempData["Success"] = $"Payment successful! You are now subscribed to the {payment.PlanName}. Please log out and log back in to activate your new plan.";
            }
            else
            {
                payment.Status = status ?? "Unknown";
                await _context.SaveChangesAsync();

                TempData["Error"] = $"Payment status: {status}. Please try again or contact support.";
            }

            return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
        }

        public IActionResult Cancel()
        {
            TempData["Error"] = "Payment was cancelled.";
            return RedirectToAction("UserNav", "UserAdmin", new { section = "subscription" });
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
                return RedirectToAction("Login", "Home");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return RedirectToAction("Login", "Home");

            var payments = await _context.Payments
                .Where(p => p.OrganizationID == user.OrganizationID)
                .OrderByDescending(p => p.Created_at)
                .ToListAsync();

            return Json(payments.Select(p => new
            {
                p.PaymentID,
                p.PlanName,
                p.Amount,
                p.Currency,
                p.Status,
                p.Created_at,
                p.Paid_at
            }));
        }
    }
}
