using it15_webproject_mvc.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace it15_webproject_mvc.Services
{
    public class RetentionPolicyService : BackgroundService
    {
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RetentionPolicyService> _logger;

        public RetentionPolicyService(IServiceScopeFactory scopeFactory, ILogger<RetentionPolicyService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ApplyRetentionAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RetentionPolicyService failed while applying retention.");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        private async Task ApplyRetentionAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var auditRetentionDays = await GetRetentionDaysAsync(context, "AuditLogRetentionDays", 730, cancellationToken);
            var historyRetentionDays = await GetRetentionDaysAsync(context, "HistoricalRetentionDays", 365, cancellationToken);

            var now = DateTime.UtcNow;
            var auditCutoff = now.AddDays(-auditRetentionDays);
            var historyCutoff = now.AddDays(-historyRetentionDays);

            var auditRemoved = await context.AuditLogs
                .Where(a => a.Performed_at < auditCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var systemRemoved = await context.SystemLogs
                .Where(s => s.Action_timestamp < auditCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var securityRemoved = await context.SecurityLogs
                .Where(s => s.Created_at < auditCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var historicalRemoved = await context.HistoricalData
                .Where(h => (h.Retention_until.HasValue && h.Retention_until.Value < now)
                    || (!h.Retention_until.HasValue && h.Snapshot_date < historyCutoff))
                .ExecuteDeleteAsync(cancellationToken);

            var totalRemoved = auditRemoved + systemRemoved + securityRemoved + historicalRemoved;
            if (totalRemoved > 0)
            {
                _logger.LogInformation(
                    "RetentionPolicyService removed {Total} records (Audit: {Audit}, System: {System}, Security: {Security}, Historical: {Historical}).",
                    totalRemoved,
                    auditRemoved,
                    systemRemoved,
                    securityRemoved,
                    historicalRemoved);
            }
        }

        private static async Task<int> GetRetentionDaysAsync(ApplicationDbContext context, string key, int defaultValue, CancellationToken cancellationToken)
        {
            var config = await context.SystemConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConfigKey == key, cancellationToken);

            if (config == null || !int.TryParse(config.ConfigValue, out var days) || days <= 0)
            {
                return defaultValue;
            }

            return days;
        }
    }
}
