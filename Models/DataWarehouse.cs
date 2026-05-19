using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace it15_webproject_mvc.Models
{
    public class DataSource
    {
        [Key]
        public int DataSourceID { get; set; }

        [Required, MaxLength(100)]
        public string SourceName { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string ApiBaseUrl { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ApiEndpoint { get; set; } = string.Empty;

        [MaxLength(500)]
        public string ApiKey { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string AuthMethod { get; set; } = "ApiKey"; // ApiKey, Bearer, Basic, None

        [MaxLength(100)]
        public string TargetTable { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Status { get; set; } = "Active"; // Active, Inactive, Error

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        public DateTime? Last_sync { get; set; }

        public int CreatedByUserID { get; set; }

        public int OrganizationID { get; set; }

        [ForeignKey(nameof(CreatedByUserID))]
        public User? CreatedByUser { get; set; }

        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }

        public ICollection<StagingRecord> StagingRecords { get; set; } = [];

        public ICollection<DataSourceField> Fields { get; set; } = [];
    }

    /// <summary>
    /// Describes the field/column metadata for a data source.
    /// </summary>
    public class DataSourceField
    {
        [Key]
        public int FieldID { get; set; }

        public int DataSourceID { get; set; }

        [Required, MaxLength(100)]
        public string FieldName { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string DataType { get; set; } = "string";

        public int? FieldLength { get; set; }

        public bool IsRequired { get; set; } = false;

        [ForeignKey(nameof(DataSourceID))]
        public DataSource? DataSource { get; set; }
    }

    public class StagingRecord
    {
        [Key]
        public int StagingRecordID { get; set; }

        public int DataSourceID { get; set; }

        [Required, MaxLength(100)]
        public string BatchId { get; set; } = string.Empty;

        [Required]
        public string RawData { get; set; } = string.Empty; // JSON blob of the row

        public int RowNumber { get; set; }

        [Required, MaxLength(20)]
        public string ValidationStatus { get; set; } = "Pending"; // Pending, Valid, Warning, Error

        [MaxLength(500)]
        public string? ValidationMessage { get; set; }

        public DateTime Pulled_at { get; set; } = DateTime.UtcNow;

        public int PulledByUserID { get; set; }

        [ForeignKey(nameof(DataSourceID))]
        public DataSource? DataSource { get; set; }

        [ForeignKey(nameof(PulledByUserID))]
        public User? PulledByUser { get; set; }

        public int? SubmissionID { get; set; }

        [ForeignKey(nameof(SubmissionID))]
        public DataSubmission? Submission { get; set; }
    }

    /// <summary>
    /// Dedicated error log for upload/validation errors — one row per error per record.
    /// </summary>
    public class UploadErrorLog
    {
        [Key]
        public int ErrorID { get; set; }

        public int StagingRecordID { get; set; }

        [Required, MaxLength(500)]
        public string ErrorDescription { get; set; } = string.Empty;

        public DateTime Error_timestamp { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(StagingRecordID))]
        public StagingRecord? StagingRecord { get; set; }
    }

    public class AuditLog
    {
        [Key]
        public int AuditLogID { get; set; }

        [Required, MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string EntityType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        [MaxLength(200)]
        public string? EntityName { get; set; }

        [MaxLength(1000)]
        public string? Details { get; set; }

        public int PerformedByUserID { get; set; }

        public int OrganizationID { get; set; }

        [ForeignKey(nameof(PerformedByUserID))]
        public User? PerformedByUser { get; set; }

        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }

        public DateTime Performed_at { get; set; } = DateTime.UtcNow;
    }

    public class DataSubmission
    {
        [Key]
        public int SubmissionID { get; set; }

        [Required, MaxLength(100)]
        public string BatchId { get; set; } = string.Empty;

        public int DataSourceID { get; set; }

        [Required, MaxLength(100)]
        public string TargetTable { get; set; } = string.Empty;

        public int TotalRows { get; set; }

        public int ValidRows { get; set; }

        public int ErrorRows { get; set; }

        [Required, MaxLength(20)]
        public string LoadMode { get; set; } = "Append"; // Append, Overwrite, Upsert

        [Required, MaxLength(30)]
        public string Status { get; set; } = "Pending"; // Pending, Verified, Submitted, Integrated, Failed

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        public DateTime? Submitted_at { get; set; }

        public DateTime? Integrated_at { get; set; }

        public int LoadedRows { get; set; }

        public int SkippedRows { get; set; }

        public int SubmittedByUserID { get; set; }

        public int OrganizationID { get; set; }

        [ForeignKey(nameof(DataSourceID))]
        public DataSource? DataSource { get; set; }

        [ForeignKey(nameof(SubmittedByUserID))]
        public User? SubmittedByUser { get; set; }

        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }

        public ICollection<StagingRecord> StagingRecords { get; set; } = [];

        public ICollection<EtlStageLog> ETLStageLogs { get; set; } = [];
    }

    /// <summary>
    /// Tracks each stage of the ETL pipeline for a submission:
    /// Pull ? Validate ? Cleanse ? Submit ? Integrate
    /// </summary>
    [Table("ETLStageLogs")]
    public class EtlStageLog
    {
        [Key]
        public int StageLogID { get; set; }

        public int SubmissionID { get; set; }

        [Required, MaxLength(50)]
        public string StageName { get; set; } = string.Empty; // Pull, Validate, Cleanse, Submit, Integrate

        [Required, MaxLength(20)]
        public string Status { get; set; } = "Running"; // Running, Completed, Failed

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(SubmissionID))]
        public DataSubmission? Submission { get; set; }
    }

    /// <summary>
    /// Configurable data cleansing rules stored in the database.
    /// </summary>
    public class DataCleansingRule
    {
        [Key]
        public int RuleID { get; set; }

        [Required, MaxLength(100)]
        public string RuleName { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string RuleType { get; set; } = string.Empty; // TrimWhitespace, RemoveNulls, FormatDate, etc.

        [MaxLength(500)]
        public string? RuleDescription { get; set; }

        public ICollection<DataCleansingLog> CleansingLogs { get; set; } = [];
    }

    /// <summary>
    /// Logs each cleansing action applied during data processing.
    /// </summary>
    public class DataCleansingLog
    {
        [Key]
        public int CleansingLogID { get; set; }

        public int SubmissionID { get; set; }

        public int RuleID { get; set; }

        public int AffectedRecords { get; set; }

        [Required, MaxLength(50)]
        public string CorrectionType { get; set; } = string.Empty; // Trimmed, Removed, Formatted, etc.

        public DateTime Cleansing_date { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(SubmissionID))]
        public DataSubmission? Submission { get; set; }

        [ForeignKey(nameof(RuleID))]
        public DataCleansingRule? Rule { get; set; }
    }

    /// <summary>
    /// Final warehouse table — stores cleansed, flattened data after Manager approval.
    /// Each row represents one record that has been fully processed through the ETL pipeline.
    /// </summary>
    public class WarehouseRecord
    {
        [Key]
        public int WarehouseRecordID { get; set; }

        public int DataSourceID { get; set; }

        public int SubmissionID { get; set; }

        [Required, MaxLength(100)]
        public string BatchId { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string TargetTable { get; set; } = string.Empty;

        public int RowNumber { get; set; }

        /// <summary>
        /// The cleansed JSON data that passed validation.
        /// </summary>
        [Required]
        public string CleanData { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string CleanDataHash { get; set; } = string.Empty;

        /// <summary>
        /// Snapshot of the original raw data before cleansing, for auditing.
        /// </summary>
        [Required]
        public string RawDataSnapshot { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string RawDataSnapshotHash { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string RecordStatus { get; set; } = "Active"; // Active, Archived, Deleted

        [Required, MaxLength(20)]
        public string LoadMode { get; set; } = "Append"; // Append, Overwrite, Upsert

        public int Version { get; set; } = 1;

        public DateTime Loaded_at { get; set; } = DateTime.UtcNow;

        public int LoadedByUserID { get; set; }

        public int OrganizationID { get; set; }

        [ForeignKey(nameof(DataSourceID))]
        public DataSource? DataSource { get; set; }

        [ForeignKey(nameof(SubmissionID))]
        public DataSubmission? Submission { get; set; }

        [ForeignKey(nameof(LoadedByUserID))]
        public User? LoadedByUser { get; set; }

        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }
    }

    /// <summary>
    /// Stores versioned snapshots of warehouse data for historical tracking and rollback.
    /// </summary>
    public class HistoricalData
    {
        [Key]
        public int HistoricalDataID { get; set; }

        public int WarehouseRecordID { get; set; }

        public int VersionNo { get; set; }

        public DateTime Snapshot_date { get; set; } = DateTime.UtcNow;

        [Required]
        public string DataPayload { get; set; } = string.Empty;

        public DateTime? Retention_until { get; set; }

        [ForeignKey(nameof(WarehouseRecordID))]
        public WarehouseRecord? WarehouseRecord { get; set; }
    }

    /// <summary>
    /// In-app notification for users — triggered by system events (submissions, approvals, etc.).
    /// </summary>
    public class Notification
    {
        [Key]
        public int NotificationID { get; set; }

        public int UserID { get; set; }

        public int OrganizationID { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Message { get; set; }

        [Required, MaxLength(50)]
        public string Type { get; set; } = "Info"; // Info, Success, Warning, Error

        [MaxLength(500)]
        public string? Link { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserID))]
        public User? User { get; set; }

        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }
    }
}
