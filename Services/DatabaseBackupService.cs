using it15_webproject_mvc.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.RegularExpressions;

namespace it15_webproject_mvc.Services
{
    public class DatabaseBackupService : BackgroundService
    {
        private static readonly Regex DatabaseNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
        private static readonly TimeSpan RunInterval = TimeSpan.FromDays(1);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseBackupService> _logger;

        public DatabaseBackupService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<DatabaseBackupService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunBackupAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DatabaseBackupService failed while running backup.");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        private async Task RunBackupAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!context.Database.IsSqlServer())
            {
                return;
            }

            var backupSettings = _configuration.GetSection("BackupSettings");
            var backupPath = backupSettings["BackupPath"]?.Trim();
            var retentionDaysValue = backupSettings["RetentionDays"];

            if (string.IsNullOrWhiteSpace(backupPath))
            {
                _logger.LogWarning("DatabaseBackupService skipped because BackupSettings:BackupPath is not configured.");
                return;
            }

            if (!int.TryParse(retentionDaysValue, out var retentionDays) || retentionDays <= 0)
            {
                retentionDays = 30;
            }

            Directory.CreateDirectory(backupPath);

            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("DatabaseBackupService skipped because no SQL Server connection string was found.");
                return;
            }

            var builder = new SqlConnectionStringBuilder(connectionString);
            var databaseName = builder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.LogWarning("DatabaseBackupService skipped because the connection string has no database name.");
                return;
            }

            if (!DatabaseNamePattern.IsMatch(databaseName))
            {
                _logger.LogWarning("DatabaseBackupService skipped because the database name contains invalid characters.");
                return;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{databaseName}_{timestamp}.bak";
            var backupFilePath = Path.Combine(backupPath, backupFileName);

            var safeDatabaseName = new SqlCommandBuilder().QuoteIdentifier(databaseName);
            var backupCommand = $@"
 BACKUP DATABASE {safeDatabaseName}
 TO DISK = @backupPath
 WITH INIT, COMPRESSION, CHECKSUM;";

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = new SqlCommand(backupCommand, connection)
                {
                    CommandTimeout = 0
                };
                command.Parameters.Add(new SqlParameter("@backupPath", SqlDbType.NVarChar, 260)
                {
                    Value = backupFilePath
                });
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            CleanupOldBackups(backupPath, databaseName, retentionDays);
            _logger.LogInformation("Database backup completed: {BackupFile}", backupFilePath);
        }

        private void CleanupOldBackups(string backupPath, string databaseName, int retentionDays)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var prefix = databaseName + "_";

            foreach (var file in Directory.EnumerateFiles(backupPath, "*.bak"))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var created = File.GetCreationTimeUtc(file);
                if (created < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old backup {BackupFile}", file);
                    }
                }
            }
        }
    }
}
