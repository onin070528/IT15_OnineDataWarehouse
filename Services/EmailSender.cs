using System.Net;
using System.Net.Mail;

namespace it15_webproject_mvc.Services
{
    public interface IEmailSender
    {
        Task<bool> SendAsync(string toEmail, string subject, string body);
    }

    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<bool> SendAsync(string toEmail, string subject, string body)
        {
            var host = _configuration["EmailSettings:Host"];
            var portValue = _configuration["EmailSettings:Port"];
            var username = _configuration["EmailSettings:Username"];
            var password = _configuration["EmailSettings:Password"];
            var fromEmail = _configuration["EmailSettings:FromEmail"];
            var fromName = _configuration["EmailSettings:FromName"];
            var useSslValue = _configuration["EmailSettings:UseSsl"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
            {
                return false;
            }

            if (!int.TryParse(portValue, out var port))
            {
                port = 587;
            }

            _ = bool.TryParse(useSslValue, out var useSsl);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            var fromAddress = string.IsNullOrWhiteSpace(fromName)
                ? new MailAddress(fromEmail)
                : new MailAddress(fromEmail, fromName);

            using var message = new MailMessage(fromAddress, new MailAddress(toEmail))
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            try
            {
                await client.SendMailAsync(message);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
