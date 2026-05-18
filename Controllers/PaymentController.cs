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
        private const string ActionLogin = "Login";
        private const string ControllerHome = "Home";
        private const string ActionUserNav = "UserNav";
        private const string ControllerUserAdmin = "UserAdmin";
        private const string SectionSubscription = "subscription";
        private const string TempDataError = "Error";
        private const string TempDataSuccess = "Success";
        private const string CurrencyPhp = "PHP";
        private const string PaymentStatusPending = "Pending";
        private const string PaymentStatusPaid = "Paid";
        private const string PaymentStatusUnknown = "Unknown";
        private const string SubscriptionPremium = "Premium";
        private const string SubscriptionBasic = "Basic";
        private const string PayMongoStatusActive = "active";
        private const string PayMongoStatusPaid = "paid";

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
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var userId = GetCurrentUserId();
            if (userId == 0)
                return RedirectToAction(ActionLogin, ControllerHome);

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return RedirectToAction(ActionLogin, ControllerHome);

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
                    TempData[TempDataError] = "Invalid plan selected.";
                    return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var successUrl = $"{baseUrl}/Payment/Success";
            var cancelUrl = $"{baseUrl}/Payment/Cancel";

            var (checkoutUrl, sessionId, error) = await _payMongoService.CreateCheckoutSession(
                planName, amountInCentavos, CurrencyPhp, description, successUrl, cancelUrl);

            if (error != null || checkoutUrl == null)
            {
                _logger.LogError("Failed to create checkout: {Error}", error);
                TempData[TempDataError] = "Failed to initiate payment. Please try again.";
                return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
            }

            var payment = new Payment
            {
                UserID = userId,
                OrganizationID = user.OrganizationID,
                PlanName = planName,
                Amount = amountInCentavos / 100m,
                Currency = CurrencyPhp,
                Status = PaymentStatusPending,
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
                return RedirectToAction(ActionLogin, ControllerHome);

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
                    .Where(p => p.UserID == userId && p.Status == PaymentStatusPending)
                    .OrderByDescending(p => p.Created_at)
                    .FirstOrDefaultAsync();
            }

            if (payment == null)
            {
                TempData[TempDataError] = "Payment record not found.";
                return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
            }

            var checkoutSessionId = payment.CheckoutSessionId;
            if (string.IsNullOrEmpty(checkoutSessionId))
            {
                TempData[TempDataError] = "Invalid payment session.";
                return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
            }

            var (status, paymentId, error) = await _payMongoService.GetCheckoutSession(checkoutSessionId);

            if (error != null)
            {
                _logger.LogError("Failed to verify payment: {Error}", error);
                TempData[TempDataError] = "Failed to verify payment. Please contact support.";
                return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
            }

            if (status == PayMongoStatusActive || status == PayMongoStatusPaid)
            {
                payment.Status = PaymentStatusPaid;
                payment.PayMongoPaymentId = paymentId;
                payment.Paid_at = DateTime.UtcNow;

                // Update organization subscription plan
                var org = await _context.Organizations.FindAsync(payment.OrganizationID);
                if (org != null)
                {
                    org.SubscriptionPlan = payment.PlanName.Contains(SubscriptionPremium) ? SubscriptionPremium : SubscriptionBasic;
                }

                await _context.SaveChangesAsync();

                TempData[TempDataSuccess] = $"Payment successful! You are now subscribed to the {payment.PlanName}. Please log out and log back in to activate your new plan.";
            }
            else
            {
                payment.Status = status ?? PaymentStatusUnknown;
                await _context.SaveChangesAsync();

                TempData[TempDataError] = $"Payment status: {status}. Please try again or contact support.";
            }

            return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
        }

        public IActionResult Cancel()
        {
            TempData[TempDataError] = "Payment was cancelled.";
            return RedirectToAction(ActionUserNav, ControllerUserAdmin, new { section = SectionSubscription });
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
                return RedirectToAction(ActionLogin, ControllerHome);

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return RedirectToAction(ActionLogin, ControllerHome);

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
