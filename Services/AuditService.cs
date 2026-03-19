using it15_webproject_mvc.Data;
using it15_webproject_mvc.Models;

namespace it15_webproject_mvc.Services
{
    public interface IAuditService
    {
        /// <summary>
        /// Logs an audit entry. Call SaveChangesAsync on the context after all work is done
        /// to commit the audit log along with other changes in a single unit of work.
        /// </summary>
        void Log(string action, string entityType, int? entityId, string? entityName, string? details, int performedByUserId, int organizationId);
    }

    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;

        public AuditService(ApplicationDbContext context)
        {
            _context = context;
        }

        public void Log(string action, string entityType, int? entityId, string? entityName, string? details, int performedByUserId, int organizationId)
        {
            var auditLog = new AuditLog
            {
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                EntityName = entityName,
                Details = details,
                PerformedByUserID = performedByUserId,
                OrganizationID = organizationId,
                Performed_at = DateTime.UtcNow
            };

            _context.AuditLogs.Add(auditLog);
        }
    }
}
