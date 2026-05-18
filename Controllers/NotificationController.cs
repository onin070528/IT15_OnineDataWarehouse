using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using it15_webproject_mvc.Data;

namespace it15_webproject_mvc.Controllers
{
    [Route("api/notifications")]
    public class NotificationController : BaseController
    {
        private readonly ApplicationDbContext _context;

        public NotificationController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetNotifications()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Unauthorized();

            var notifications = await _context.Notifications
                .Where(n => n.UserID == userId)
                .OrderByDescending(n => n.Created_at)
                .Take(20)
                .Select(n => new
                {
                    n.NotificationID,
                    n.Title,
                    n.Message,
                    n.Type,
                    n.Link,
                    n.IsRead,
                    CreatedAt = n.Created_at.ToString("MMM dd, yyyy hh:mm tt"),
                    TimeAgo = GetTimeAgo(n.Created_at)
                })
                .ToListAsync();

            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserID == userId && !n.IsRead);

            return Json(new { notifications, unreadCount });
        }

        [HttpPost("read/{id}")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var userId = GetCurrentUserId();
            var notif = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationID == id && n.UserID == userId);

            if (notif == null) return NotFound();

            notif.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var userId = GetCurrentUserId();
            await _context.Notifications
                .Where(n => n.UserID == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

            return Ok();
        }

        private static string GetTimeAgo(DateTime created)
        {
            var span = DateTime.UtcNow - created;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return created.ToString("MMM dd");
        }
    }
}
