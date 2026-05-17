using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace it15_webproject_mvc.Models
{
    public class Role
    {
        [Key]
        public int RoleID { get; set; }

        [Required, MaxLength(30)]
        public string RoleName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Description { get; set; }

        public int RoleLevel { get; set; } = 0;

        public ICollection<User> Users { get; set; } = [];

        public ICollection<RolePermission> RolePermissions { get; set; } = [];
    }

    public class Permission
    {
        [Key]
        public int PermissionID { get; set; }

        [Required, MaxLength(50)]
        public string PermissionCode { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string PermissionName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string ModuleName { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Description { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; } = [];
    }

    /// <summary>
    /// Junction table: many-to-many between Role and Permission
    /// </summary>
    public class RolePermission
    {
        [Key]
        public int RolePermissionID { get; set; }

        public int RoleID { get; set; }

        public int PermissionID { get; set; }

        [ForeignKey(nameof(RoleID))]
        public Role? Role { get; set; }

        [ForeignKey(nameof(PermissionID))]
        public Permission? Permission { get; set; }
    }

    public class Organization
    {
        [Key]
        public int OrganizationID { get; set; }

        [Required, MaxLength(50)]
        public string OrganizationName { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string SubscriptionPlan { get; set; } = "Free";

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        public ICollection<User> Users { get; set; } = [];
    }

    public class User
    {
        [Key]
        public int UserID { get; set; }

        // Foreign Keys
        public int OrganizationID { get; set; }
        public int RoleID { get; set; }

        [Required, MaxLength(15)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(25)]
        public string Full_name { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Account_status { get; set; } = "Active";

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutUntil { get; set; }

        [MaxLength(200)]
        public string? TwoFactorCodeHash { get; set; }

        public DateTime? TwoFactorCodeExpiresAt { get; set; }

        public DateTime? Last_login { get; set; }

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }

        [ForeignKey(nameof(RoleID))]
        public Role? Role { get; set; }
    }

    /// <summary>
    /// Tracks system-level events: logins, logouts, module access, errors.
    /// Separate from AuditLog which tracks data/entity changes.
    /// </summary>
    public class SystemLog
    {
        [Key]
        public int LogID { get; set; }

        public int UserID { get; set; }

        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        public DateTime Action_timestamp { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserID))]
        public User? User { get; set; }
    }

    public class SecurityLog
    {
        [Key]
        public int SecurityLogID { get; set; }

        public int? UserID { get; set; }

        [Required, MaxLength(100)]
        public string EventType { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Details { get; set; }

        [MaxLength(100)]
        public string? IpAddress { get; set; }

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserID))]
        public User? User { get; set; }
    }

    /// <summary>
    /// Stores application-level configuration key-value pairs in the database.
    /// </summary>
    public class SystemConfiguration
    {
        [Key]
        public int ConfigID { get; set; }

        [Required, MaxLength(100)]
        public string ConfigKey { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string ConfigValue { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Description { get; set; }

        public DateTime Updated_at { get; set; } = DateTime.UtcNow;
    }
}
