using System.Security.Claims;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Controllers
{
    [Route("auth")]
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuthController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe = false)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Redirect("/Home/Login?error=missing");
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.Username == username || u.Email == username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                return Redirect("/Home/Login?error=invalid");
            }

            if (user.Account_status != "Active")
            {
                return Redirect("/Home/Login?error=Account is disabled.");
            }

            // Update last login and log the event
            user.Last_login = DateTime.UtcNow;

            _context.AuditLogs.Add(new AuditLog
            {
                Action = "Login",
                EntityType = "User",
                EntityId = user.UserID,
                EntityName = user.Username,
                Details = $"User '{user.Full_name}' logged in (Role: {user.Role?.RoleName ?? "Unknown"})",
                PerformedByUserID = user.UserID,
                OrganizationID = user.OrganizationID,
                Performed_at = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            var roleName = user.Role?.RoleName ?? "Staff";
            var orgName = user.Organization?.OrganizationName ?? "My Company";
            var subPlan = user.Organization?.SubscriptionPlan ?? "Free";

            // Enforce subscription-based role access on login
            if (roleName == "Manager" && subPlan == "Free")
            {
                return Redirect("/Home/Login?error=Your organization is on the Free plan. The Manager role requires a Basic or Premium subscription.");
            }
            if (roleName == "DataAnalyst" && subPlan != "Premium")
            {
                return Redirect("/Home/Login?error=Your organization requires a Premium subscription for the Data Analyst role.");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("FullName", user.Full_name),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, roleName),
                new("OrganizationID", user.OrganizationID.ToString()),
                new("OrganizationName", orgName),
                new("SubscriptionPlan", subPlan)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe
            };

            if (rememberMe)
            {
                authProperties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
            }

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);

            // Redirect based on role
            return roleName switch
            {
                "SuperAdmin" => RedirectToAction("Supernav", "SuperAdmin"),
                "UserAdmin" => RedirectToAction("UserNav", "UserAdmin"),
                "Staff" => RedirectToAction("StaffNav", "Staff"),
                "DataAnalyst" => RedirectToAction("AnalystNav", "DataAnalyst"),
                "Manager" => RedirectToAction("ManagerNav", "Manager"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        [HttpGet("logout")]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId);
                if (user != null)
                {
                    _context.AuditLogs.Add(new AuditLog
                    {
                        Action = "Logout",
                        EntityType = "User",
                        EntityId = user.UserID,
                        EntityName = user.Username,
                        Details = $"User '{user.Full_name}' logged out",
                        PerformedByUserID = user.UserID,
                        OrganizationID = user.OrganizationID,
                        Performed_at = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Home");
        }

        [HttpPost("register")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string companyName, string fullName, string username, string email, string password, string confirmPassword)
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                return Redirect("/Home/Register?error=missing");
            }

            // Validate password match
            if (password != confirmPassword)
            {
                return Redirect("/Home/Register?error=password_mismatch");
            }

            // Validate password strength: minimum 12 characters and at least one special character
            if (password.Length < 12 || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return Redirect("/Home/Register?error=Password must be at least 12 characters and contain at least one special character.");
            }

            // Check if username already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username);
            if (existingUser != null)
            {
                return Redirect("/Home/Register?error=username_taken");
            }

            // Check if email already exists
            var existingEmail = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);
            if (existingEmail != null)
            {
                return Redirect("/Home/Register?error=email_taken");
            }

            // Get or create the UserAdmin role
            var userAdminRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.RoleName == "UserAdmin");
            if (userAdminRole == null)
            {
                userAdminRole = new Models.Role { RoleName = "UserAdmin" };
                _context.Roles.Add(userAdminRole);
                await _context.SaveChangesAsync();
            }

            // Create the organization (company)
            var organization = new Models.Organization
            {
                OrganizationName = companyName.Trim()
            };
            _context.Organizations.Add(organization);
            await _context.SaveChangesAsync();

            // Create the user with UserAdmin role
            var user = new Models.User
            {
                OrganizationID = organization.OrganizationID,
                RoleID = userAdminRole.RoleID,
                Username = username.Trim(),
                Full_name = fullName.Trim(),
                Email = email.Trim(),
                Password = BCrypt.Net.BCrypt.HashPassword(password),
                Account_status = "Active",
                Created_at = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                Action = "User Created",
                EntityType = "User",
                EntityId = user.UserID,
                EntityName = user.Username,
                Details = $"New organization '{companyName.Trim()}' registered by '{fullName.Trim()}' (UserAdmin)",
                PerformedByUserID = user.UserID,
                OrganizationID = organization.OrganizationID,
                Performed_at = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Redirect("/Home/Login?success=registered");
        }
    }
}
