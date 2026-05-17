using Microsoft.EntityFrameworkCore;
using it15_webproject_mvc.Models;

namespace it15_webproject_mvc.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        // === Identity & Access ===
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<User> Users { get; set; }

        // === Data Ingestion ===
        public DbSet<DataSource> DataSources { get; set; }
        public DbSet<DataSourceField> DataSourceFields { get; set; }
        public DbSet<StagingRecord> StagingRecords { get; set; }
        public DbSet<UploadErrorLog> UploadErrorLogs { get; set; }

        // === ETL Pipeline ===
        public DbSet<DataSubmission> DataSubmissions { get; set; }
        public DbSet<ETLStageLog> ETLStageLogs { get; set; }

        // === Data Quality ===
        public DbSet<DataCleansingRule> DataCleansingRules { get; set; }
        public DbSet<DataCleansingLog> DataCleansingLogs { get; set; }

        // === Warehouse ===
        public DbSet<WarehouseRecord> WarehouseRecords { get; set; }
        public DbSet<HistoricalData> HistoricalData { get; set; }

        // === Audit & System ===
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<SecurityLog> SecurityLogs { get; set; }
        public DbSet<SystemConfiguration> SystemConfigurations { get; set; }

        // === Billing ===
        public DbSet<Payment> Payments { get; set; }

        // === Notifications ===
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // === ROLE PERMISSION (junction table) ===
            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RolePermission>()
                .HasIndex(rp => new { rp.RoleID, rp.PermissionID })
                .IsUnique()
                .HasDatabaseName("IX_RolePermissions_RoleId_PermissionId");

            // === DATA SOURCE FIELD ===
            modelBuilder.Entity<DataSourceField>()
                .HasOne(f => f.DataSource)
                .WithMany(d => d.Fields)
                .HasForeignKey(f => f.DataSourceID)
                .OnDelete(DeleteBehavior.Cascade);

            // === UPLOAD ERROR LOG ===
            modelBuilder.Entity<UploadErrorLog>()
                .HasOne(e => e.StagingRecord)
                .WithMany()
                .HasForeignKey(e => e.StagingRecordID)
                .OnDelete(DeleteBehavior.Cascade);

            // === ETL STAGE LOG ===
            modelBuilder.Entity<ETLStageLog>()
                .HasOne(l => l.Submission)
                .WithMany(s => s.ETLStageLogs)
                .HasForeignKey(l => l.SubmissionID)
                .OnDelete(DeleteBehavior.Cascade);

            // === DATA CLEANSING LOG ===
            modelBuilder.Entity<DataCleansingLog>()
                .HasOne(cl => cl.Submission)
                .WithMany()
                .HasForeignKey(cl => cl.SubmissionID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DataCleansingLog>()
                .HasOne(cl => cl.Rule)
                .WithMany(r => r.CleansingLogs)
                .HasForeignKey(cl => cl.RuleID)
                .OnDelete(DeleteBehavior.Restrict);

            // === HISTORICAL DATA ===
            modelBuilder.Entity<HistoricalData>()
                .HasOne(h => h.WarehouseRecord)
                .WithMany()
                .HasForeignKey(h => h.WarehouseRecordID)
                .OnDelete(DeleteBehavior.Cascade);

            // === SYSTEM LOG ===
            modelBuilder.Entity<SystemLog>()
                .HasOne(sl => sl.User)
                .WithMany()
                .HasForeignKey(sl => sl.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SecurityLog>()
                .HasOne(sl => sl.User)
                .WithMany()
                .HasForeignKey(sl => sl.UserID)
                .OnDelete(DeleteBehavior.SetNull);

            // === SYSTEM CONFIGURATION ===
            modelBuilder.Entity<SystemConfiguration>()
                .HasIndex(sc => sc.ConfigKey)
                .IsUnique()
                .HasDatabaseName("IX_SystemConfigurations_ConfigKey");

            // === EXISTING RELATIONSHIPS ===

            // DataSource ? User (CreatedByUserID)
            modelBuilder.Entity<DataSource>()
                .HasOne(d => d.CreatedByUser)
                .WithMany()
                .HasForeignKey(d => d.CreatedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // StagingRecord ? DataSource
            modelBuilder.Entity<StagingRecord>()
                .HasOne(s => s.DataSource)
                .WithMany(d => d.StagingRecords)
                .HasForeignKey(s => s.DataSourceID)
                .OnDelete(DeleteBehavior.Restrict);

            // StagingRecord ? User (PulledByUserID)
            modelBuilder.Entity<StagingRecord>()
                .HasOne(s => s.PulledByUser)
                .WithMany()
                .HasForeignKey(s => s.PulledByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // StagingRecord ? DataSubmission
            modelBuilder.Entity<StagingRecord>()
                .HasOne(s => s.Submission)
                .WithMany(d => d.StagingRecords)
                .HasForeignKey(s => s.SubmissionID)
                .OnDelete(DeleteBehavior.SetNull);

            // DataSubmission ? DataSource
            modelBuilder.Entity<DataSubmission>()
                .HasOne(s => s.DataSource)
                .WithMany()
                .HasForeignKey(s => s.DataSourceID)
                .OnDelete(DeleteBehavior.Restrict);

            // DataSubmission ? User (SubmittedByUserID)
            modelBuilder.Entity<DataSubmission>()
                .HasOne(s => s.SubmittedByUser)
                .WithMany()
                .HasForeignKey(s => s.SubmittedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // AuditLog ? User (PerformedByUserID)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.PerformedByUser)
                .WithMany()
                .HasForeignKey(a => a.PerformedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // DataSource ? Organization
            modelBuilder.Entity<DataSource>()
                .HasOne(d => d.Organization)
                .WithMany()
                .HasForeignKey(d => d.OrganizationID)
                .OnDelete(DeleteBehavior.Restrict);

            // DataSubmission ? Organization
            modelBuilder.Entity<DataSubmission>()
                .HasOne(s => s.Organization)
                .WithMany()
                .HasForeignKey(s => s.OrganizationID)
                .OnDelete(DeleteBehavior.Restrict);

            // AuditLog ? Organization
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.Organization)
                .WithMany()
                .HasForeignKey(a => a.OrganizationID)
                .OnDelete(DeleteBehavior.Restrict);

            // Payment ? User
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // Payment ? Organization
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Organization)
                .WithMany()
                .HasForeignKey(p => p.OrganizationID)
                .OnDelete(DeleteBehavior.Restrict);

            // WarehouseRecord ? DataSource
            modelBuilder.Entity<WarehouseRecord>()
                .HasOne(w => w.DataSource)
                .WithMany()
                .HasForeignKey(w => w.DataSourceID)
                .OnDelete(DeleteBehavior.Restrict);

            // WarehouseRecord ? DataSubmission
            modelBuilder.Entity<WarehouseRecord>()
                .HasOne(w => w.Submission)
                .WithMany()
                .HasForeignKey(w => w.SubmissionID)
                .OnDelete(DeleteBehavior.Restrict);

            // WarehouseRecord ? User (LoadedByUserID)
            modelBuilder.Entity<WarehouseRecord>()
                .HasOne(w => w.LoadedByUser)
                .WithMany()
                .HasForeignKey(w => w.LoadedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // WarehouseRecord ? Organization
            modelBuilder.Entity<WarehouseRecord>()
                .HasOne(w => w.Organization)
                .WithMany()
                .HasForeignKey(w => w.OrganizationID)
                .OnDelete(DeleteBehavior.Restrict);

            // === PERFORMANCE INDEXES ===

            // DataSource: filtered by OrganizationID + Status in nearly every controller
            modelBuilder.Entity<DataSource>()
                .HasIndex(d => new { d.OrganizationID, d.Status })
                .HasDatabaseName("IX_DataSources_OrgId_Status");

            // DataSourceField: looked up by DataSourceID
            modelBuilder.Entity<DataSourceField>()
                .HasIndex(f => f.DataSourceID)
                .HasDatabaseName("IX_DataSourceFields_DataSourceId");

            // StagingRecord: filtered by DataSourceID, BatchId, ValidationStatus, SubmissionID
            modelBuilder.Entity<StagingRecord>()
                .HasIndex(s => new { s.DataSourceID, s.BatchId })
                .HasDatabaseName("IX_StagingRecords_DataSourceId_BatchId");
            modelBuilder.Entity<StagingRecord>()
                .HasIndex(s => new { s.ValidationStatus, s.DataSourceID })
                .HasDatabaseName("IX_StagingRecords_ValidationStatus_DataSourceId");
            modelBuilder.Entity<StagingRecord>()
                .HasIndex(s => s.SubmissionID)
                .HasDatabaseName("IX_StagingRecords_SubmissionId");

            // UploadErrorLog: looked up by StagingRecordID
            modelBuilder.Entity<UploadErrorLog>()
                .HasIndex(e => e.StagingRecordID)
                .HasDatabaseName("IX_UploadErrorLogs_StagingRecordId");

            // DataSubmission: filtered by OrganizationID + Status
            modelBuilder.Entity<DataSubmission>()
                .HasIndex(s => new { s.OrganizationID, s.Status })
                .HasDatabaseName("IX_DataSubmissions_OrgId_Status");
            modelBuilder.Entity<DataSubmission>()
                .HasIndex(s => new { s.OrganizationID, s.Created_at })
                .HasDatabaseName("IX_DataSubmissions_OrgId_CreatedAt");

            // ETLStageLog: looked up by SubmissionID
            modelBuilder.Entity<ETLStageLog>()
                .HasIndex(l => l.SubmissionID)
                .HasDatabaseName("IX_ETLStageLogs_SubmissionId");

            // DataCleansingLog: looked up by SubmissionID, RuleID
            modelBuilder.Entity<DataCleansingLog>()
                .HasIndex(cl => cl.SubmissionID)
                .HasDatabaseName("IX_DataCleansingLogs_SubmissionId");
            modelBuilder.Entity<DataCleansingLog>()
                .HasIndex(cl => cl.RuleID)
                .HasDatabaseName("IX_DataCleansingLogs_RuleId");

            // AuditLog: filtered by OrganizationID, ordered by Performed_at
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.OrganizationID, a.Performed_at })
                .HasDatabaseName("IX_AuditLogs_OrgId_PerformedAt");
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.EntityType)
                .HasDatabaseName("IX_AuditLogs_EntityType");

            // WarehouseRecord: filtered by OrganizationID + RecordStatus + TargetTable
            modelBuilder.Entity<WarehouseRecord>()
                .HasIndex(w => new { w.OrganizationID, w.RecordStatus, w.TargetTable })
                .HasDatabaseName("IX_WarehouseRecords_OrgId_Status_Table");

            // HistoricalData: looked up by WarehouseRecordID
            modelBuilder.Entity<HistoricalData>()
                .HasIndex(h => h.WarehouseRecordID)
                .HasDatabaseName("IX_HistoricalData_WarehouseRecordId");

            // SystemLog: looked up by UserID, timestamp
            modelBuilder.Entity<SystemLog>()
                .HasIndex(sl => new { sl.UserID, sl.Action_timestamp })
                .HasDatabaseName("IX_SystemLogs_UserId_Timestamp");

            // Payment: filtered by OrganizationID, Status
            modelBuilder.Entity<Payment>()
                .HasIndex(p => new { p.OrganizationID, p.Status })
                .HasDatabaseName("IX_Payments_OrgId_Status");
            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.CheckoutSessionId)
                .HasDatabaseName("IX_Payments_CheckoutSessionId");

            // Notification ? User
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.Organization)
                .WithMany()
                .HasForeignKey(n => n.OrganizationID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.UserID, n.IsRead, n.Created_at })
                .HasDatabaseName("IX_Notifications_UserId_IsRead_CreatedAt");

            // User: looked up by Username, Email
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("IX_Users_Username");
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasDatabaseName("IX_Users_Email");
            modelBuilder.Entity<User>()
                .HasIndex(u => u.OrganizationID)
                .HasDatabaseName("IX_Users_OrgId");

            // Permission: looked up by PermissionCode
            modelBuilder.Entity<Permission>()
                .HasIndex(p => p.PermissionCode)
                .IsUnique()
                .HasDatabaseName("IX_Permissions_PermissionCode");
        }
    }
}
