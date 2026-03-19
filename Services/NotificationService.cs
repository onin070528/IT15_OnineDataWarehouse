using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Services
{
    public interface INotificationService
    {
        /// <summary>
        /// Creates a notification for a specific user.
        /// Call SaveChangesAsync on the context to persist.
        /// </summary>
        void Notify(int userId, int organizationId, string title, string? message, string type = "Info", string? link = null);

        /// <summary>
        /// Creates a notification for all users in an organization with a specific role.
        /// Saves immediately.
        /// </summary>
        Task NotifyRoleAsync(int organizationId, string roleName, string title, string? message, string type = "Info", string? link = null);
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;

        public NotificationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public void Notify(int userId, int organizationId, string title, string? message, string type = "Info", string? link = null)
        {
            _context.Notifications.Add(new Notification
            {
                UserID = userId,
                OrganizationID = organizationId,
                Title = title,
                Message = message,
                Type = type,
                Link = link,
                Created_at = DateTime.UtcNow
            });
        }

        public async Task NotifyRoleAsync(int organizationId, string roleName, string title, string? message, string type = "Info", string? link = null)
        {
            var userIds = await _context.Users
                .Where(u => u.OrganizationID == organizationId && u.Role != null && u.Role.RoleName == roleName && u.Account_status == "Active")
                .Select(u => u.UserID)
                .ToListAsync();

            foreach (var uid in userIds)
            {
                _context.Notifications.Add(new Notification
                {
                    UserID = uid,
                    OrganizationID = organizationId,
                    Title = title,
                    Message = message,
                    Type = type,
                    Link = link,
                    Created_at = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }
    }
}
