using it15_webproject_mvc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Mail;

namespace it15_webproject_mvc.Data
{
    public static class DbInitializer
    {
        private static bool IsSqlServer(ApplicationDbContext context)
        {
            return context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
        }

        public static void Initialize(ApplicationDbContext context, IConfiguration configuration, ILogger? logger = null)
        {
            // Ensure the database and schema exist without deleting existing data
            logger?.LogInformation("DbInitializer: Calling EnsureCreated...");
            var created = context.Database.EnsureCreated();
            logger?.LogInformation("DbInitializer: EnsureCreated returned {Created}", created);

            var superAdminEmail = ResolveSuperAdminEmail(configuration, logger);

            // Add new tables/columns that EnsureCreated won't add to an existing database
            EnsureWarehouseTable(context);
            EnsureSubmissionNewColumns(context);
            EnsureNewTables(context);
            EnsureRoleNewColumns(context);
            EnsureOrganizationNewColumns(context);
            EnsureUserNewColumns(context);

            logger?.LogInformation("DbInitializer: Existing users count = {Count}", context.Users.Count());

            if (!context.Roles.Any())
            {
                var roles = new Role[]
                {
                    new() { RoleName = "SuperAdmin", Description = "Full system access across all organizations", RoleLevel = 100 },
                    new() { RoleName = "UserAdmin", Description = "Organization administrator with user and subscription management", RoleLevel = 80 },
                    new() { RoleName = "DataAnalyst", Description = "Read-only analytics, security auditing, and data quality review", RoleLevel = 60 },
                    new() { RoleName = "Manager", Description = "Approve/reject submissions and manage ETL pipeline", RoleLevel = 40 },
                    new() { RoleName = "Staff", Description = "Pull data from APIs, validate, and submit batches", RoleLevel = 20 }
                };
                context.Roles.AddRange(roles);
                context.SaveChanges();
            }

            // Seed Permissions
            if (!context.Permissions.Any())
            {
                var permissions = new Permission[]
                {
                    new() { PermissionCode = "DASH_VIEW", PermissionName = "View Dashboard", ModuleName = "Dashboard", Description = "Access the dashboard overview" },
                    new() { PermissionCode = "SRC_VIEW", PermissionName = "View Sources", ModuleName = "DataSources", Description = "View API data sources" },
                    new() { PermissionCode = "SRC_CREATE", PermissionName = "Create Source", ModuleName = "DataSources", Description = "Add new API data sources" },
                    new() { PermissionCode = "SRC_DELETE", PermissionName = "Delete Source", ModuleName = "DataSources", Description = "Remove API data sources" },
                    new() { PermissionCode = "SRC_TOGGLE", PermissionName = "Toggle Source Status", ModuleName = "DataSources", Description = "Activate/deactivate data sources" },
                    new() { PermissionCode = "DATA_PULL", PermissionName = "Pull Data", ModuleName = "DataUpload", Description = "Pull data from API sources into staging" },
                    new() { PermissionCode = "DATA_VALIDATE", PermissionName = "Validate Data", ModuleName = "DataUpload", Description = "Validate staging records" },
                    new() { PermissionCode = "DATA_CORRECT", PermissionName = "Correct Records", ModuleName = "DataUpload", Description = "Correct flagged staging records" },
                    new() { PermissionCode = "DATA_SUBMIT", PermissionName = "Submit Batch", ModuleName = "DataUpload", Description = "Submit validated batches for approval" },
                    new() { PermissionCode = "SUB_APPROVE", PermissionName = "Approve Submission", ModuleName = "Approvals", Description = "Approve and load submissions into warehouse" },
                    new() { PermissionCode = "SUB_REJECT", PermissionName = "Reject Submission", ModuleName = "Approvals", Description = "Reject submissions" },
                    new() { PermissionCode = "WH_VIEW", PermissionName = "View Warehouse", ModuleName = "Warehouse", Description = "Browse warehouse data" },
                    new() { PermissionCode = "WH_ARCHIVE", PermissionName = "Archive Table", ModuleName = "Warehouse", Description = "Archive warehouse table data" },
                    new() { PermissionCode = "WH_RESTORE", PermissionName = "Restore Table", ModuleName = "Warehouse", Description = "Restore archived warehouse data" },
                    new() { PermissionCode = "RPT_VIEW", PermissionName = "View Reports", ModuleName = "Reports", Description = "Access reports and analytics" },
                    new() { PermissionCode = "AUDIT_VIEW", PermissionName = "View Audit Logs", ModuleName = "AuditTrail", Description = "View audit and activity logs" },
                    new() { PermissionCode = "USR_VIEW", PermissionName = "View Users", ModuleName = "UserManagement", Description = "View organization users" },
                    new() { PermissionCode = "USR_CREATE", PermissionName = "Create User", ModuleName = "UserManagement", Description = "Add new users to organization" },
                    new() { PermissionCode = "USR_MANAGE", PermissionName = "Manage Users", ModuleName = "UserManagement", Description = "Edit/disable users across all organizations" },
                    new() { PermissionCode = "SUB_MANAGE", PermissionName = "Manage Subscription", ModuleName = "Subscription", Description = "Upgrade/manage subscription plans" },
                    new() { PermissionCode = "SYS_ADMIN", PermissionName = "System Administration", ModuleName = "System", Description = "Full system-wide administration" },
                    new() { PermissionCode = "CLNS_VIEW", PermissionName = "View Cleansing", ModuleName = "DataCleansing", Description = "View data quality and cleansing results" },
                    new() { PermissionCode = "SEC_VIEW", PermissionName = "View Security", ModuleName = "Security", Description = "View security audit and user access logs" },
                };
                context.Permissions.AddRange(permissions);
                context.SaveChanges();
            }

            // Seed RolePermissions (junction)
            if (!context.RolePermissions.Any())
            {
                var allPerms = context.Permissions.ToList();
                var roles = context.Roles.ToList();

                var superAdmin = roles.First(r => r.RoleName == "SuperAdmin");
                var userAdmin = roles.First(r => r.RoleName == "UserAdmin");
                var analyst = roles.First(r => r.RoleName == "DataAnalyst");
                var manager = roles.First(r => r.RoleName == "Manager");
                var staff = roles.First(r => r.RoleName == "Staff");

                var rolePermissions = new List<RolePermission>();

                // SuperAdmin gets ALL permissions
                foreach (var p in allPerms)
                    rolePermissions.Add(new() { RoleID = superAdmin.RoleID, PermissionID = p.PermissionID });

                // UserAdmin
                var userAdminCodes = new[] { "DASH_VIEW", "SRC_VIEW", "SRC_TOGGLE", "WH_VIEW", "WH_ARCHIVE", "WH_RESTORE", "RPT_VIEW", "AUDIT_VIEW", "USR_VIEW", "USR_CREATE", "SUB_MANAGE", "CLNS_VIEW" };
                foreach (var code in userAdminCodes)
                {
                    var p = allPerms.FirstOrDefault(x => x.PermissionCode == code);
                    if (p != null) rolePermissions.Add(new() { RoleID = userAdmin.RoleID, PermissionID = p.PermissionID });
                }

                // DataAnalyst
                var analystCodes = new[] { "DASH_VIEW", "SRC_VIEW", "WH_VIEW", "RPT_VIEW", "AUDIT_VIEW", "CLNS_VIEW", "SEC_VIEW" };
                foreach (var code in analystCodes)
                {
                    var p = allPerms.FirstOrDefault(x => x.PermissionCode == code);
                    if (p != null) rolePermissions.Add(new() { RoleID = analyst.RoleID, PermissionID = p.PermissionID });
                }

                // Manager
                var managerCodes = new[] { "DASH_VIEW", "SRC_VIEW", "WH_VIEW", "RPT_VIEW", "AUDIT_VIEW", "SUB_APPROVE", "SUB_REJECT" };
                foreach (var code in managerCodes)
                {
                    var p = allPerms.FirstOrDefault(x => x.PermissionCode == code);
                    if (p != null) rolePermissions.Add(new() { RoleID = manager.RoleID, PermissionID = p.PermissionID });
                }

                // Staff
                var staffCodes = new[] { "DASH_VIEW", "SRC_VIEW", "SRC_CREATE", "SRC_DELETE", "DATA_PULL", "DATA_VALIDATE", "DATA_CORRECT", "DATA_SUBMIT", "RPT_VIEW", "AUDIT_VIEW" };
                foreach (var code in staffCodes)
                {
                    var p = allPerms.FirstOrDefault(x => x.PermissionCode == code);
                    if (p != null) rolePermissions.Add(new() { RoleID = staff.RoleID, PermissionID = p.PermissionID });
                }

                context.RolePermissions.AddRange(rolePermissions);
                context.SaveChanges();
            }

            // Seed Organizations
            if (!context.Organizations.Any())
            {
                var orgs = new Organization[]
                {
                    new() { OrganizationName = "Neyo System" },
                    new() { OrganizationName = "Default Org" }
                };
                context.Organizations.AddRange(orgs);
                context.SaveChanges();
            }

            // Seed Users (one per role)
            if (!context.Users.Any())
            {
                logger?.LogInformation("DbInitializer: Seeding users...");
                var superAdminRole = context.Roles.First(r => r.RoleName == "SuperAdmin");
                var userAdminRole = context.Roles.First(r => r.RoleName == "UserAdmin");
                var analystRole = context.Roles.First(r => r.RoleName == "DataAnalyst");
                var managerRole = context.Roles.First(r => r.RoleName == "Manager");
                var staffRole = context.Roles.First(r => r.RoleName == "Staff");

                var neyoOrg = context.Organizations.First(o => o.OrganizationName == "Neyo System");
                var defaultOrg = context.Organizations.First(o => o.OrganizationName == "Default Org");

                var users = new User[]
                {
                    new()
                    {
                        OrganizationID = neyoOrg.OrganizationID,
                        RoleID = superAdminRole.RoleID,
                        Username = "superadmin",
                        Full_name = "Super Admin",
                        Email = superAdminEmail,
                        Password = BCrypt.Net.BCrypt.HashPassword("SuperPassword123"),
                        Account_status = "Active",
                        Last_login = null,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        OrganizationID = neyoOrg.OrganizationID,
                        RoleID = userAdminRole.RoleID,
                        Username = "useradmin",
                        Full_name = "User Admin",
                        Email = "useradmin@example.com",
                        Password = BCrypt.Net.BCrypt.HashPassword("UserAdminPass123"),
                        Account_status = "Active",
                        Last_login = null,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        OrganizationID = defaultOrg.OrganizationID,
                        RoleID = analystRole.RoleID,
                        Username = "analyst",
                        Full_name = "Data Analyst",
                        Email = "analyst@example.com",
                        Password = BCrypt.Net.BCrypt.HashPassword("AnalystPass123"),
                        Account_status = "Active",
                        Last_login = null,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        OrganizationID = defaultOrg.OrganizationID,
                        RoleID = managerRole.RoleID,
                        Username = "manager",
                        Full_name = "Project Manager",
                        Email = "manager@example.com",
                        Password = BCrypt.Net.BCrypt.HashPassword("ManagerPass123"),
                        Account_status = "Active",
                        Last_login = null,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        OrganizationID = defaultOrg.OrganizationID,
                        RoleID = staffRole.RoleID,
                        Username = "staff",
                        Full_name = "Staff Member",
                        Email = "staff@example.com",
                        Password = BCrypt.Net.BCrypt.HashPassword("StaffPass123"),
                        Account_status = "Active",
                        Last_login = null,
                        Created_at = DateTime.UtcNow
                    }
                };

                context.Users.AddRange(users);
                context.SaveChanges();
                logger?.LogInformation("DbInitializer: Users seeded successfully. Count = {Count}", context.Users.Count());
            }
            else
            {
                logger?.LogInformation("DbInitializer: Users already exist, ensuring seeded account passwords are correct...");

                var existingSuperAdmin = context.Users.FirstOrDefault(u => u.Username == "superadmin");
                if (existingSuperAdmin != null && !IsValidEmail(existingSuperAdmin.Email))
                {
                    logger?.LogWarning("DbInitializer: SuperAdmin email is invalid. Updating to configured seed email.");
                    existingSuperAdmin.Email = superAdminEmail;
                    context.SaveChanges();
                }

                // Map of seeded usernames to their expected passwords
                var seededAccounts = new Dictionary<string, string>
                {
                    { "superadmin", "SuperPassword123" },
                    { "useradmin", "UserAdminPass123" },
                    { "analyst", "AnalystPass123" },
                    { "manager", "ManagerPass123" },
                    { "staff", "StaffPass123" }
                };

                foreach (var (username, expectedPassword) in seededAccounts)
                {
                    var user = context.Users.FirstOrDefault(u => u.Username == username);
                    if (user == null)
                    {
                        logger?.LogWarning("DbInitializer: Seeded user '{Username}' not found in database.", username);
                        continue;
                    }

                    bool passwordValid = false;
                    try
                    {
                        passwordValid = BCrypt.Net.BCrypt.Verify(expectedPassword, user.Password);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "DbInitializer: BCrypt verify failed for '{Username}', password hash may be corrupted. Resetting...", username);
                        passwordValid = false;
                    }

                    if (!passwordValid)
                    {
                        logger?.LogInformation("DbInitializer: Resetting password for '{Username}'...", username);
                        user.Password = BCrypt.Net.BCrypt.HashPassword(expectedPassword);
                        user.Account_status = "Active";
                        context.SaveChanges();
                        logger?.LogInformation("DbInitializer: Password reset successfully for '{Username}'.", username);
                    }
                }
            }

            // Seed Data Sources (sample company API endpoints)
            if (!context.DataSources.Any())
            {
                var staffUser = context.Users.First(u => u.Username == "staff");

                var dataSources = new DataSource[]
                {
                    new()
                    {
                        SourceName = "JSONPlaceholder - Users",
                        ApiBaseUrl = "https://jsonplaceholder.typicode.com",
                        ApiEndpoint = "/users",
                        ApiKey = "",
                        AuthMethod = "None",
                        TargetTable = "stg_crm_contacts",
                        Status = "Active",
                        CreatedByUserID = staffUser.UserID,
                        OrganizationID = staffUser.OrganizationID,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        SourceName = "JSONPlaceholder - Posts",
                        ApiBaseUrl = "https://jsonplaceholder.typicode.com",
                        ApiEndpoint = "/posts",
                        ApiKey = "",
                        AuthMethod = "None",
                        TargetTable = "stg_content_feed",
                        Status = "Active",
                        CreatedByUserID = staffUser.UserID,
                        OrganizationID = staffUser.OrganizationID,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        SourceName = "JSONPlaceholder - Comments",
                        ApiBaseUrl = "https://jsonplaceholder.typicode.com",
                        ApiEndpoint = "/comments",
                        ApiKey = "",
                        AuthMethod = "None",
                        TargetTable = "stg_feedback",
                        Status = "Active",
                        CreatedByUserID = staffUser.UserID,
                        OrganizationID = staffUser.OrganizationID,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        SourceName = "JSONPlaceholder - Todos",
                        ApiBaseUrl = "https://jsonplaceholder.typicode.com",
                        ApiEndpoint = "/todos",
                        ApiKey = "",
                        AuthMethod = "None",
                        TargetTable = "stg_task_tracker",
                        Status = "Active",
                        CreatedByUserID = staffUser.UserID,
                        OrganizationID = staffUser.OrganizationID,
                        Created_at = DateTime.UtcNow
                    },
                    new()
                    {
                        SourceName = "JSONPlaceholder - Albums",
                        ApiBaseUrl = "https://jsonplaceholder.typicode.com",
                        ApiEndpoint = "/albums",
                        ApiKey = "",
                        AuthMethod = "None",
                        TargetTable = "stg_media_catalog",
                        Status = "Active",
                        CreatedByUserID = staffUser.UserID,
                        OrganizationID = staffUser.OrganizationID,
                        Created_at = DateTime.UtcNow
                    }
                };

                context.DataSources.AddRange(dataSources);
                context.SaveChanges();
            }

            // Seed Data Cleansing Rules
            if (!context.DataCleansingRules.Any())
            {
                var rules = new DataCleansingRule[]
                {
                    new() { RuleName = "Trim Whitespace", RuleType = "TrimWhitespace", RuleDescription = "Remove leading and trailing whitespace from string values" },
                    new() { RuleName = "Remove Nulls", RuleType = "RemoveNulls", RuleDescription = "Remove fields with null values from records" },
                    new() { RuleName = "Remove Empty Strings", RuleType = "RemoveEmpty", RuleDescription = "Remove fields with empty string values" },
                };
                context.DataCleansingRules.AddRange(rules);
                context.SaveChanges();
            }

            // Seed System Configuration
            if (!context.SystemConfigurations.Any())
            {
                var configs = new SystemConfiguration[]
                {
                    new() { ConfigKey = "MaxApiTimeout", ConfigValue = "30", Description = "Maximum API request timeout in seconds" },
                    new() { ConfigKey = "MaxStagingRowsPerBatch", ConfigValue = "10000", Description = "Maximum rows allowed per staging batch" },
                    new() { ConfigKey = "DefaultLoadMode", ConfigValue = "Append", Description = "Default load mode for warehouse integration" },
                    new() { ConfigKey = "HistoricalRetentionDays", ConfigValue = "365", Description = "Number of days to retain historical data snapshots" },
                    new() { ConfigKey = "AuditLogRetentionDays", ConfigValue = "730", Description = "Number of days to retain audit logs" },
                    new() { ConfigKey = "SuperAdminLockdownEnabled", ConfigValue = "false", Description = "Block SuperAdmin access during incident response" },
                    new() { ConfigKey = "SuperAdminLockdownReason", ConfigValue = "", Description = "Reason for SuperAdmin lockdown" },
                };
                context.SystemConfigurations.AddRange(configs);
                context.SaveChanges();
            }

            EnsureSecurityConfigurations(context);
        }

        private static string ResolveSuperAdminEmail(IConfiguration configuration, ILogger? logger)
        {
            var configuredEmail = configuration["SeedSettings:SuperAdminEmail"]?.Trim();
            var fallbackEmail = configuration["EmailSettings:FromEmail"]?.Trim();
            var usernameEmail = configuration["EmailSettings:Username"]?.Trim();

            if (IsValidEmail(configuredEmail))
            {
                return configuredEmail!;
            }

            if (IsValidEmail(fallbackEmail))
            {
                return fallbackEmail!;
            }

            if (IsValidEmail(usernameEmail))
            {
                return usernameEmail!;
            }

            logger?.LogWarning("DbInitializer: No valid SuperAdmin email configured. Falling back to superadmin@example.com.");
            return "superadmin@example.com";
        }

        private static bool IsValidEmail(string? email)
        {
            return MailAddress.TryCreate(email, out _);
        }

        private static void EnsureWarehouseTable(ApplicationDbContext context)
        {
            try
            {
                context.WarehouseRecords.Any();
            }
            catch
            {
                if (IsSqlServer(context))
                {
                    context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WarehouseRecords' AND xtype='U')
                        CREATE TABLE WarehouseRecords (
                            WarehouseRecordID INT IDENTITY(1,1) PRIMARY KEY,
                            DataSourceID INT NOT NULL,
                            SubmissionID INT NOT NULL,
                            BatchId NVARCHAR(100) NOT NULL,
                            TargetTable NVARCHAR(100) NOT NULL,
                            RowNumber INT NOT NULL DEFAULT 0,
                            CleanData NVARCHAR(MAX) NOT NULL,
                            RawDataSnapshot NVARCHAR(MAX) NOT NULL,
                            RecordStatus NVARCHAR(20) NOT NULL DEFAULT 'Active',
                            LoadMode NVARCHAR(20) NOT NULL DEFAULT 'Append',
                            Version INT NOT NULL DEFAULT 1,
                            Loaded_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            LoadedByUserID INT NOT NULL,
                            OrganizationID INT NOT NULL,
                            CONSTRAINT FK_WarehouseRecords_DataSource FOREIGN KEY (DataSourceID) REFERENCES DataSources(DataSourceID),
                            CONSTRAINT FK_WarehouseRecords_Submission FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID),
                            CONSTRAINT FK_WarehouseRecords_User FOREIGN KEY (LoadedByUserID) REFERENCES Users(UserID),
                            CONSTRAINT FK_WarehouseRecords_Org FOREIGN KEY (OrganizationID) REFERENCES Organizations(OrganizationID)
                        )");
                }
                else
                {
                    context.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS WarehouseRecords (
                            WarehouseRecordID INTEGER PRIMARY KEY AUTOINCREMENT,
                            DataSourceID INTEGER NOT NULL, SubmissionID INTEGER NOT NULL,
                            BatchId TEXT NOT NULL, TargetTable TEXT NOT NULL,
                            RowNumber INTEGER NOT NULL DEFAULT 0, CleanData TEXT NOT NULL,
                            RawDataSnapshot TEXT NOT NULL, RecordStatus TEXT NOT NULL DEFAULT 'Active',
                            LoadMode TEXT NOT NULL DEFAULT 'Append', Version INTEGER NOT NULL DEFAULT 1,
                            Loaded_at TEXT NOT NULL DEFAULT (datetime('now')),
                            LoadedByUserID INTEGER NOT NULL, OrganizationID INTEGER NOT NULL,
                            FOREIGN KEY (DataSourceID) REFERENCES DataSources(DataSourceID),
                            FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID),
                            FOREIGN KEY (LoadedByUserID) REFERENCES Users(UserID),
                            FOREIGN KEY (OrganizationID) REFERENCES Organizations(OrganizationID)
                        )");
                }
            }
        }

        private static void EnsureSubmissionNewColumns(ApplicationDbContext context)
        {
            if (IsSqlServer(context))
            {
                try
                {
                    context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DataSubmissions') AND name = 'Integrated_at')
                        ALTER TABLE DataSubmissions ADD Integrated_at DATETIME2 NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DataSubmissions') AND name = 'LoadedRows')
                        ALTER TABLE DataSubmissions ADD LoadedRows INT NOT NULL DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DataSubmissions') AND name = 'SkippedRows')
                        ALTER TABLE DataSubmissions ADD SkippedRows INT NOT NULL DEFAULT 0;");
                }
                catch { }
            }
            else
            {
                try { context.Database.ExecuteSqlRaw("ALTER TABLE DataSubmissions ADD COLUMN Integrated_at TEXT NULL"); } catch { }
                try { context.Database.ExecuteSqlRaw("ALTER TABLE DataSubmissions ADD COLUMN LoadedRows INTEGER NOT NULL DEFAULT 0"); } catch { }
                try { context.Database.ExecuteSqlRaw("ALTER TABLE DataSubmissions ADD COLUMN SkippedRows INTEGER NOT NULL DEFAULT 0"); } catch { }
            }
        }

        /// <summary>
        /// Creates the 9 new tables added to the detailed database design.
        /// </summary>
        private static void EnsureNewTables(ApplicationDbContext context)
        {
            if (IsSqlServer(context))
            {
                try { context.Permissions.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Permissions' AND xtype='U') CREATE TABLE Permissions (PermissionID INT IDENTITY(1,1) PRIMARY KEY, PermissionCode NVARCHAR(50) NOT NULL, PermissionName NVARCHAR(100) NOT NULL, ModuleName NVARCHAR(100) NOT NULL, Description NVARCHAR(300) NULL)"); }

                try { context.RolePermissions.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RolePermissions' AND xtype='U') CREATE TABLE RolePermissions (RolePermissionID INT IDENTITY(1,1) PRIMARY KEY, RoleID INT NOT NULL, PermissionID INT NOT NULL, CONSTRAINT FK_RolePermissions_Role FOREIGN KEY (RoleID) REFERENCES Roles(RoleID) ON DELETE CASCADE, CONSTRAINT FK_RolePermissions_Permission FOREIGN KEY (PermissionID) REFERENCES Permissions(PermissionID) ON DELETE CASCADE)"); }

                try { context.DataSourceFields.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataSourceFields' AND xtype='U') CREATE TABLE DataSourceFields (FieldID INT IDENTITY(1,1) PRIMARY KEY, DataSourceID INT NOT NULL, FieldName NVARCHAR(100) NOT NULL, DataType NVARCHAR(50) NOT NULL DEFAULT 'string', FieldLength INT NULL, IsRequired BIT NOT NULL DEFAULT 0, CONSTRAINT FK_DataSourceFields_DataSource FOREIGN KEY (DataSourceID) REFERENCES DataSources(DataSourceID) ON DELETE CASCADE)"); }

                try { context.UploadErrorLogs.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UploadErrorLogs' AND xtype='U') CREATE TABLE UploadErrorLogs (ErrorID INT IDENTITY(1,1) PRIMARY KEY, StagingRecordID INT NOT NULL, ErrorDescription NVARCHAR(500) NOT NULL, Error_timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_UploadErrorLogs_StagingRecord FOREIGN KEY (StagingRecordID) REFERENCES StagingRecords(StagingRecordID) ON DELETE CASCADE)"); }

                try { context.ETLStageLogs.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ETLStageLogs' AND xtype='U') CREATE TABLE ETLStageLogs (StageLogID INT IDENTITY(1,1) PRIMARY KEY, SubmissionID INT NOT NULL, StageName NVARCHAR(50) NOT NULL, Status NVARCHAR(20) NOT NULL DEFAULT 'Running', Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_ETLStageLogs_Submission FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID) ON DELETE CASCADE)"); }

                try { context.DataCleansingRules.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataCleansingRules' AND xtype='U') CREATE TABLE DataCleansingRules (RuleID INT IDENTITY(1,1) PRIMARY KEY, RuleName NVARCHAR(100) NOT NULL, RuleType NVARCHAR(50) NOT NULL, RuleDescription NVARCHAR(500) NULL)"); }

                try { context.DataCleansingLogs.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataCleansingLogs' AND xtype='U') CREATE TABLE DataCleansingLogs (CleansingLogID INT IDENTITY(1,1) PRIMARY KEY, SubmissionID INT NOT NULL, RuleID INT NOT NULL, AffectedRecords INT NOT NULL DEFAULT 0, CorrectionType NVARCHAR(50) NOT NULL, Cleansing_date DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_DataCleansingLogs_Submission FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID), CONSTRAINT FK_DataCleansingLogs_Rule FOREIGN KEY (RuleID) REFERENCES DataCleansingRules(RuleID))"); }

                try { context.HistoricalData.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='HistoricalData' AND xtype='U') CREATE TABLE HistoricalData (HistoricalDataID INT IDENTITY(1,1) PRIMARY KEY, WarehouseRecordID INT NOT NULL, VersionNo INT NOT NULL, Snapshot_date DATETIME2 NOT NULL DEFAULT GETUTCDATE(), DataPayload NVARCHAR(MAX) NOT NULL, Retention_until DATETIME2 NULL, CONSTRAINT FK_HistoricalData_WarehouseRecord FOREIGN KEY (WarehouseRecordID) REFERENCES WarehouseRecords(WarehouseRecordID) ON DELETE CASCADE)"); }

                try { context.SystemLogs.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemLogs' AND xtype='U') CREATE TABLE SystemLogs (LogID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NOT NULL, Module NVARCHAR(100) NOT NULL, Action NVARCHAR(100) NOT NULL, Action_timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_SystemLogs_User FOREIGN KEY (UserID) REFERENCES Users(UserID))"); }

                try { context.SecurityLogs.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SecurityLogs' AND xtype='U') CREATE TABLE SecurityLogs (SecurityLogID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NULL, EventType NVARCHAR(100) NOT NULL, Details NVARCHAR(1000) NULL, IpAddress NVARCHAR(100) NULL, Created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_SecurityLogs_User FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE SET NULL)"); }

                try { context.SystemConfigurations.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemConfigurations' AND xtype='U') CREATE TABLE SystemConfigurations (ConfigID INT IDENTITY(1,1) PRIMARY KEY, ConfigKey NVARCHAR(100) NOT NULL, ConfigValue NVARCHAR(500) NOT NULL, Description NVARCHAR(300) NULL, Updated_at DATETIME2 NOT NULL DEFAULT GETUTCDATE())"); }

                try { context.Notifications.Any(); }
                catch { context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Notifications' AND xtype='U') CREATE TABLE Notifications (NotificationID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NOT NULL, OrganizationID INT NOT NULL, Title NVARCHAR(200) NOT NULL, Message NVARCHAR(500) NULL, Type NVARCHAR(50) NOT NULL DEFAULT 'Info', Link NVARCHAR(500) NULL, IsRead BIT NOT NULL DEFAULT 0, Created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_Notifications_User FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE CASCADE, CONSTRAINT FK_Notifications_Org FOREIGN KEY (OrganizationID) REFERENCES Organizations(OrganizationID))"); }
            }
            else
            {
                // SQLite fallback
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Permissions (PermissionID INTEGER PRIMARY KEY AUTOINCREMENT, PermissionCode TEXT NOT NULL, PermissionName TEXT NOT NULL, ModuleName TEXT NOT NULL, Description TEXT NULL)");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS RolePermissions (RolePermissionID INTEGER PRIMARY KEY AUTOINCREMENT, RoleID INTEGER NOT NULL, PermissionID INTEGER NOT NULL, FOREIGN KEY (RoleID) REFERENCES Roles(RoleID), FOREIGN KEY (PermissionID) REFERENCES Permissions(PermissionID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS DataSourceFields (FieldID INTEGER PRIMARY KEY AUTOINCREMENT, DataSourceID INTEGER NOT NULL, FieldName TEXT NOT NULL, DataType TEXT NOT NULL DEFAULT 'string', FieldLength INTEGER NULL, IsRequired INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (DataSourceID) REFERENCES DataSources(DataSourceID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS UploadErrorLogs (ErrorID INTEGER PRIMARY KEY AUTOINCREMENT, StagingRecordID INTEGER NOT NULL, ErrorDescription TEXT NOT NULL, Error_timestamp TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (StagingRecordID) REFERENCES StagingRecords(StagingRecordID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS ETLStageLogs (StageLogID INTEGER PRIMARY KEY AUTOINCREMENT, SubmissionID INTEGER NOT NULL, StageName TEXT NOT NULL, Status TEXT NOT NULL DEFAULT 'Running', Timestamp TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS DataCleansingRules (RuleID INTEGER PRIMARY KEY AUTOINCREMENT, RuleName TEXT NOT NULL, RuleType TEXT NOT NULL, RuleDescription TEXT NULL)");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS DataCleansingLogs (CleansingLogID INTEGER PRIMARY KEY AUTOINCREMENT, SubmissionID INTEGER NOT NULL, RuleID INTEGER NOT NULL, AffectedRecords INTEGER NOT NULL DEFAULT 0, CorrectionType TEXT NOT NULL, Cleansing_date TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID), FOREIGN KEY (RuleID) REFERENCES DataCleansingRules(RuleID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS HistoricalData (HistoricalDataID INTEGER PRIMARY KEY AUTOINCREMENT, WarehouseRecordID INTEGER NOT NULL, VersionNo INTEGER NOT NULL, Snapshot_date TEXT NOT NULL DEFAULT (datetime('now')), DataPayload TEXT NOT NULL, Retention_until TEXT NULL, FOREIGN KEY (WarehouseRecordID) REFERENCES WarehouseRecords(WarehouseRecordID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS SystemLogs (LogID INTEGER PRIMARY KEY AUTOINCREMENT, UserID INTEGER NOT NULL, Module TEXT NOT NULL, Action TEXT NOT NULL, Action_timestamp TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (UserID) REFERENCES Users(UserID))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS SecurityLogs (SecurityLogID INTEGER PRIMARY KEY AUTOINCREMENT, UserID INTEGER NULL, EventType TEXT NOT NULL, Details TEXT NULL, IpAddress TEXT NULL, Created_at TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE SET NULL)");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS SystemConfigurations (ConfigID INTEGER PRIMARY KEY AUTOINCREMENT, ConfigKey TEXT NOT NULL, ConfigValue TEXT NOT NULL, Description TEXT NULL, Updated_at TEXT NOT NULL DEFAULT (datetime('now')))");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS Notifications (NotificationID INTEGER PRIMARY KEY AUTOINCREMENT, UserID INTEGER NOT NULL, OrganizationID INTEGER NOT NULL, Title TEXT NOT NULL, Message TEXT NULL, Type TEXT NOT NULL DEFAULT 'Info', Link TEXT NULL, IsRead INTEGER NOT NULL DEFAULT 0, Created_at TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (UserID) REFERENCES Users(UserID), FOREIGN KEY (OrganizationID) REFERENCES Organizations(OrganizationID))");
            }
        }

        private static void EnsureRoleNewColumns(ApplicationDbContext context)
        {
            if (IsSqlServer(context))
            {
                try
                {
                    context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Roles') AND name = 'Description')
                        ALTER TABLE Roles ADD Description NVARCHAR(200) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Roles') AND name = 'RoleLevel')
                        ALTER TABLE Roles ADD RoleLevel INT NOT NULL DEFAULT 0;");
                }
                catch { }
            }
            else
            {
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Roles ADD COLUMN Description TEXT NULL"); } catch { }
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Roles ADD COLUMN RoleLevel INTEGER NOT NULL DEFAULT 0"); } catch { }
            }
        }

        private static void EnsureOrganizationNewColumns(ApplicationDbContext context)
        {
            if (IsSqlServer(context))
            {
                try
                {
                    context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Organizations') AND name = 'Created_at')
                        ALTER TABLE Organizations ADD Created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE();");
                }
                catch { }
            }
            else
            {
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Organizations ADD COLUMN Created_at TEXT NOT NULL DEFAULT (datetime('now'))"); } catch { }
            }
        }

        private static void EnsureUserNewColumns(ApplicationDbContext context)
        {
            if (IsSqlServer(context))
            {
                try
                {
                    context.Database.ExecuteSqlRaw(@"
                        IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Email')
                        ALTER TABLE Users ALTER COLUMN Email NVARCHAR(100) NOT NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'FailedLoginAttempts')
                        ALTER TABLE Users ADD FailedLoginAttempts INT NOT NULL DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'LockoutUntil')
                        ALTER TABLE Users ADD LockoutUntil DATETIME2 NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'TwoFactorCodeHash')
                        ALTER TABLE Users ADD TwoFactorCodeHash NVARCHAR(200) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'TwoFactorCodeExpiresAt')
                        ALTER TABLE Users ADD TwoFactorCodeExpiresAt DATETIME2 NULL;");
                }
                catch { }
            }
            else
            {
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN FailedLoginAttempts INTEGER NOT NULL DEFAULT 0"); } catch { }
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN LockoutUntil TEXT NULL"); } catch { }
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN TwoFactorCodeHash TEXT NULL"); } catch { }
                try { context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN TwoFactorCodeExpiresAt TEXT NULL"); } catch { }
            }
        }

        private static void EnsureSecurityConfigurations(ApplicationDbContext context)
        {
            EnsureSystemConfig(context, "SuperAdminLockdownEnabled", "false", "Block SuperAdmin access during incident response");
            EnsureSystemConfig(context, "SuperAdminLockdownReason", "", "Reason for SuperAdmin lockdown");
        }

        private static void EnsureSystemConfig(ApplicationDbContext context, string key, string value, string description)
        {
            var existing = context.SystemConfigurations.FirstOrDefault(c => c.ConfigKey == key);
            if (existing != null)
            {
                return;
            }

            context.SystemConfigurations.Add(new SystemConfiguration
            {
                ConfigKey = key,
                ConfigValue = value,
                Description = description,
                Updated_at = DateTime.UtcNow
            });
            context.SaveChanges();
        }
    }
}
