using System.Security.Claims;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;
using it15_webproject_mvc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace it15_webproject_mvc.Controllers
{
    [Route("auth")]
    public class AuthController : Controller
    {
        private static readonly HashSet<string> BlockedPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "password1",
            "123456",
            "1234567",
            "12345678",
            "123456789",
            "12345",
            "123123",
            "111111",
            "000000",
            "qwerty",
            "qwerty123",
            "abc123",
            "letmein",
            "welcome",
            "admin",
            "iloveyou",
            "monkey",
            "dragon"
        };
        private readonly ApplicationDbContext _context;
        private readonly ITimeLimitedDataProtector _passwordResetProtector;
        private readonly ITimeLimitedDataProtector _twoFactorProtector;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEmailSender _emailSender;

        public AuthController(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            IEmailSender emailSender)
        {
            _context = context;
            _passwordResetProtector = dataProtectionProvider
                .CreateProtector("it15_webproject_mvc.password-reset")
                .ToTimeLimitedDataProtector();
            _twoFactorProtector = dataProtectionProvider
                .CreateProtector("it15_webproject_mvc.two-factor")
                .ToTimeLimitedDataProtector();
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _emailSender = emailSender;
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            string username,
            string password,
            [FromForm(Name = "g-recaptcha-response")] string recaptchaToken,
            bool rememberMe = false)
        {
            if (!ModelState.IsValid)
            {
                return RedirectWithLoginError("invalid", username);
            }

            username = username?.Trim() ?? string.Empty;
            password = password?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return RedirectWithLoginError("missing", username);
            }

            if (!IsValidLoginIdentifier(username))
            {
                return RedirectWithLoginError("invalid_username", username);
            }

            if (!await IsRecaptchaValid(recaptchaToken))
            {
                return RedirectWithLoginError("recaptcha", username);
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.Username == username || u.Email == username);

            var now = DateTime.UtcNow;

            if (user != null && user.LockoutUntil.HasValue)
            {
                if (user.LockoutUntil.Value > now)
                {
                    var lockoutUntil = Uri.EscapeDataString(user.LockoutUntil.Value.ToString("O"));
                    return Redirect($"/Home/Login?error=lockout&lockoutUntil={lockoutUntil}");
                }

                user.LockoutUntil = null;
                user.FailedLoginAttempts = 0;
            }

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                if (user != null)
                {
                    user.FailedLoginAttempts++;
                    if (user.FailedLoginAttempts >= 5)
                    {
                        user.LockoutUntil = now.AddMinutes(15);
                    }

                    await _context.SaveChangesAsync();

                    if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > now)
                    {
                        var lockoutUntil = Uri.EscapeDataString(user.LockoutUntil.Value.ToString("O"));
                        return Redirect($"/Home/Login?error=lockout&lockoutUntil={lockoutUntil}");
                    }
                }

                return RedirectWithLoginError("invalid", username);
            }

            if (user.Account_status != "Active")
            {
                return RedirectWithLoginError("Account is disabled.", username);
            }

            var roleName = user.Role?.RoleName ?? "Staff";
            var subPlan = user.Organization?.SubscriptionPlan ?? "Free";

            if (roleName == "SuperAdmin" && await IsSuperAdminLockdownEnabledAsync())
            {
                await LogSecurityEventAsync(user.UserID, "SuperAdminLoginBlocked", "SuperAdmin login blocked due to lockdown.");
                return RedirectWithLoginError("superadmin_lockdown", username);
            }

            // Enforce subscription-based role access on login
            if (roleName == "Manager" && subPlan == "Free")
            {
                return RedirectWithLoginError("Your organization is on the Free plan. The Manager role requires a Basic or Premium subscription.", username);
            }
            if (roleName == "DataAnalyst" && subPlan != "Premium")
            {
                return RedirectWithLoginError("Your organization requires a Premium subscription for the Data Analyst role.", username);
            }

            var twoFactorCode = GenerateTwoFactorCode();
            user.TwoFactorCodeHash = BCrypt.Net.BCrypt.HashPassword(twoFactorCode);
            user.TwoFactorCodeExpiresAt = DateTime.UtcNow.AddMinutes(10);
            await _context.SaveChangesAsync();

            var emailSent = await _emailSender.SendAsync(
                user.Email,
                "Your verification code",
                $"Your login verification code is {twoFactorCode}. It expires in 10 minutes.");

            if (!emailSent)
            {
                return RedirectWithLoginError("2fa_email_failed", username);
            }

            var twoFactorPayload = $"{user.UserID}|{rememberMe}";
            var twoFactorToken = _twoFactorProtector.Protect(twoFactorPayload, TimeSpan.FromMinutes(10));
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(twoFactorToken));
            return Redirect($"/auth/two-factor?token={encodedToken}");
        }

        [HttpGet("two-factor")]
        public IActionResult TwoFactor(string token, string? error = null)
        {
            ViewData["Token"] = token ?? string.Empty;
            ViewData["Error"] = error ?? string.Empty;
            return View();
        }

        [HttpPost("two-factor")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TwoFactorVerify(string token, string code)
        {
            token = token?.Trim() ?? string.Empty;
            code = code?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                return Redirect("/auth/two-factor?error=missing");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Redirect($"/auth/two-factor?token={Uri.EscapeDataString(token)}&error=missing");
            }

            string protectedToken;
            try
            {
                var bytes = WebEncoders.Base64UrlDecode(token);
                protectedToken = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return Redirect("/auth/two-factor?error=invalid");
            }

            string payload;
            try
            {
                payload = _twoFactorProtector.Unprotect(protectedToken);
            }
            catch
            {
                return Redirect("/auth/two-factor?error=expired");
            }

            var payloadParts = payload.Split('|');
            if (payloadParts.Length != 2 || !int.TryParse(payloadParts[0], out var userId) || !bool.TryParse(payloadParts[1], out var rememberMe))
            {
                return Redirect("/auth/two-factor?error=invalid");
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.UserID == userId);

            if (user == null || string.IsNullOrWhiteSpace(user.TwoFactorCodeHash) || user.TwoFactorCodeExpiresAt == null)
            {
                return Redirect("/auth/two-factor?error=invalid");
            }

            if (user.TwoFactorCodeExpiresAt.Value < DateTime.UtcNow)
            {
                return Redirect("/auth/two-factor?error=expired");
            }

            if (!BCrypt.Net.BCrypt.Verify(code, user.TwoFactorCodeHash))
            {
                return Redirect($"/auth/two-factor?token={Uri.EscapeDataString(token)}&error=invalid_code");
            }

            user.TwoFactorCodeHash = null;
            user.TwoFactorCodeExpiresAt = null;

            return await SignInAndRedirectAsync(user, rememberMe);
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

        [Authorize]
        [HttpPost("ping")]
        public IActionResult Ping()
        {
            return Ok();
        }

        [HttpPost("forgot-password")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return Redirect("/Home/ForgotPassword?error=Please enter your username or email.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == email || u.Email == email);
            if (user == null)
            {
                return Redirect("/Home/ForgotPassword?error=Account not found.");
            }

            var token = _passwordResetProtector.Protect(user.UserID.ToString(), TimeSpan.FromMinutes(30));
            var protectedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            return Redirect($"/Home/ResetPassword?token={protectedToken}");
        }

        [HttpPost("reset-password")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string token, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Redirect("/Home/ResetPassword?error=Invalid reset token.");
            }

            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                return Redirect($"/Home/ResetPassword?token={Uri.EscapeDataString(token)}&error=Please fill out all fields.");
            }

            if (password != confirmPassword)
            {
                return Redirect($"/Home/ResetPassword?token={Uri.EscapeDataString(token)}&error=Passwords do not match.");
            }

            if (password.Length < 12 || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return Redirect($"/Home/ResetPassword?token={Uri.EscapeDataString(token)}&error=Password must be at least 12 characters and contain at least one special character.");
            }

            if (IsCommonPassword(password))
            {
                return Redirect($"/Home/ResetPassword?token={Uri.EscapeDataString(token)}&error=Password is too common. Please choose a stronger password.");
            }

            int userId;
            try
            {
                var protectedBytes = WebEncoders.Base64UrlDecode(token);
                var protectedText = Encoding.UTF8.GetString(protectedBytes);
                var unprotected = _passwordResetProtector.Unprotect(protectedText);
                if (!int.TryParse(unprotected, out userId))
                {
                    return Redirect("/Home/ResetPassword?error=Invalid reset token.");
                }
            }
            catch
            {
                return Redirect("/Home/ResetPassword?error=Reset token is invalid or expired.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null)
            {
                return Redirect("/Home/ResetPassword?error=Account not found.");
            }

            if (ContainsUsernameSequence(password, user.Username))
            {
                return Redirect($"/Home/ResetPassword?token={Uri.EscapeDataString(token)}&error=Password must not contain parts of your username.");
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(password);
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;

            _context.AuditLogs.Add(new AuditLog
            {
                Action = "Password Reset",
                EntityType = "User",
                EntityId = user.UserID,
                EntityName = user.Username,
                Details = $"User '{user.Full_name}' reset their password",
                PerformedByUserID = user.UserID,
                OrganizationID = user.OrganizationID,
                Performed_at = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Redirect("/Home/Login?success=password_reset");
        }

        [HttpPost("register")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(
            string companyName,
            string fullName,
            string username,
            string email,
            string password,
            string confirmPassword,
            [FromForm(Name = "g-recaptcha-response")] string recaptchaToken)
        {
            companyName = companyName?.Trim() ?? string.Empty;
            fullName = fullName?.Trim() ?? string.Empty;
            username = username?.Trim() ?? string.Empty;
            email = email?.Trim() ?? string.Empty;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                return RedirectWithRegisterError("missing", companyName, fullName, username, email);
            }

            if (companyName.Length > 50)
            {
                return RedirectWithRegisterError("company_name_length", companyName, fullName, username, email);
            }

            if (fullName.Length > 25)
            {
                return RedirectWithRegisterError("full_name_length", companyName, fullName, username, email);
            }

            if (!IsValidUsername(username))
            {
                return RedirectWithRegisterError("invalid_username", companyName, fullName, username, email);
            }

            if (!IsValidEmail(email))
            {
                return RedirectWithRegisterError("invalid_email", companyName, fullName, username, email);
            }

            if (!await IsRecaptchaValid(recaptchaToken))
            {
                return RedirectWithRegisterError("recaptcha", companyName, fullName, username, email);
            }

            // Validate password match
            if (password != confirmPassword)
            {
                return RedirectWithRegisterError("password_mismatch", companyName, fullName, username, email);
            }

            // Validate password strength: minimum 12 characters and at least one special character
            if (password.Length < 12 || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return RedirectWithRegisterError("Password must be at least 12 characters and contain at least one special character.", companyName, fullName, username, email);
            }

            if (IsCommonPassword(password))
            {
                return RedirectWithRegisterError("password_common", companyName, fullName, username, email);
            }

            if (ContainsUsernameSequence(password, username))
            {
                return RedirectWithRegisterError("password_username", companyName, fullName, username, email);
            }

            // Check if username already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username);
            if (existingUser != null)
            {
                return RedirectWithRegisterError("username_taken", companyName, fullName, username, email);
            }

            // Check if email already exists
            var existingEmail = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);
            if (existingEmail != null)
            {
                return RedirectWithRegisterError("email_taken", companyName, fullName, username, email);
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

        private static string GenerateTwoFactorCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        }

        private async Task<IActionResult> SignInAndRedirectAsync(User user, bool rememberMe)
        {
            // Update last login and log the event
            user.Last_login = DateTime.UtcNow;
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;

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

        private static bool IsValidUsername(string username)
        {
            if (username.Length < 3 || username.Length > 15)
            {
                return false;
            }

            return username.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.');
        }

        private static bool IsValidLoginIdentifier(string identifier)
        {
            if (identifier.Length > 30)
            {
                return false;
            }

            return IsValidUsername(identifier) || IsValidEmail(identifier);
        }

        [HttpPost("superadmin-lockdown")]
        [AllowAnonymous]
        public async Task<IActionResult> EnableSuperAdminLockdown(string lockdownCode, string? reason = null)
        {
            if (!IsValidLockdownCode(lockdownCode))
            {
                return Unauthorized();
            }

            await SetSystemConfigurationAsync("SuperAdminLockdownEnabled", "true", "Block SuperAdmin access during incident response");
            await SetSystemConfigurationAsync("SuperAdminLockdownReason", reason ?? "Emergency lockdown requested.", "Reason for SuperAdmin lockdown");
            await LogSecurityEventAsync(null, "SuperAdminLockdownEnabled", reason ?? "Emergency lockdown requested.");
            return Ok(new { status = "enabled" });
        }

        [HttpPost("superadmin-unlock")]
        [AllowAnonymous]
        public async Task<IActionResult> DisableSuperAdminLockdown(string lockdownCode)
        {
            if (!IsValidLockdownCode(lockdownCode))
            {
                return Unauthorized();
            }

            await SetSystemConfigurationAsync("SuperAdminLockdownEnabled", "false", "Block SuperAdmin access during incident response");
            await SetSystemConfigurationAsync("SuperAdminLockdownReason", string.Empty, "Reason for SuperAdmin lockdown");
            await LogSecurityEventAsync(null, "SuperAdminLockdownDisabled", "Emergency lockdown cleared.");
            return Ok(new { status = "disabled" });
        }

        private bool IsValidLockdownCode(string lockdownCode)
        {
            if (string.IsNullOrWhiteSpace(lockdownCode))
            {
                return false;
            }

            var configuredCode = _configuration["SecuritySettings:SuperAdminLockdownCode"];
            if (string.IsNullOrWhiteSpace(configuredCode))
            {
                return false;
            }

            return string.Equals(lockdownCode.Trim(), configuredCode, StringComparison.Ordinal);
        }

        private async Task<bool> IsSuperAdminLockdownEnabledAsync()
        {
            var config = await _context.SystemConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConfigKey == "SuperAdminLockdownEnabled");

            return config != null && bool.TryParse(config.ConfigValue, out var enabled) && enabled;
        }

        private async Task SetSystemConfigurationAsync(string key, string value, string description)
        {
            var config = await _context.SystemConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == key);
            if (config == null)
            {
                config = new SystemConfiguration
                {
                    ConfigKey = key,
                    Description = description
                };
                _context.SystemConfigurations.Add(config);
            }

            config.ConfigValue = value;
            config.Updated_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task LogSecurityEventAsync(int? userId, string eventType, string details)
        {
            _context.SecurityLogs.Add(new SecurityLog
            {
                UserID = userId,
                EventType = eventType,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Created_at = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        private static bool IsCommonPassword(string password)
        {
            return BlockedPasswords.Contains(password.Trim());
        }

        private static bool ContainsUsernameSequence(string password, string username)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            var normalizedPassword = password.Trim().ToLowerInvariant();
            var normalizedUsername = username.Trim().ToLowerInvariant();

            if (normalizedPassword.Contains(normalizedUsername))
            {
                return true;
            }

            const int minSequenceLength = 3;
            if (normalizedUsername.Length < minSequenceLength)
            {
                return false;
            }

            for (var index = 0; index <= normalizedUsername.Length - minSequenceLength; index++)
            {
                var segment = normalizedUsername.Substring(index, minSequenceLength);
                if (normalizedPassword.Contains(segment))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidEmail(string email)
        {
            if (email.Length > 100)
            {
                return false;
            }

            try
            {
                _ = new System.Net.Mail.MailAddress(email);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IActionResult RedirectWithRegisterError(string error, string companyName, string fullName, string username, string email)
        {
            TempData["Register.CompanyName"] = companyName;
            TempData["Register.FullName"] = fullName;
            TempData["Register.Username"] = username;
            TempData["Register.Email"] = email;
            return Redirect($"/Home/Register?error={Uri.EscapeDataString(error)}");
        }

        private IActionResult RedirectWithLoginError(string error, string username)
        {
            TempData["Login.Username"] = username;
            return Redirect($"/Home/Login?error={Uri.EscapeDataString(error)}");
        }

        private async Task<bool> IsRecaptchaValid(string recaptchaToken)
        {
            if (string.IsNullOrWhiteSpace(recaptchaToken))
            {
                return false;
            }

            var secretKey = _configuration["ReCaptchaSettings:SecretKey"];
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return false;
            }

            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = secretKey,
                    ["response"] = recaptchaToken
                }));

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<RecaptchaVerificationResponse>();
            return payload?.Success == true;
        }

        private sealed class RecaptchaVerificationResponse
        {
            public bool Success { get; set; }
        }
    }
}
