using it15_webproject_mvc.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Mail;

namespace it15_webproject_mvc.Data
{
    public static class DbInitializer
    {
        private const string AccountStatusActive = "Active";
        private const string ModuleDataUpload = "DataUpload";
        private const string ModuleDataSources = "DataSources";
        private const string PermissionDashView = "DASH_VIEW";
        private const string PermissionSrcView = "SRC_VIEW";
        private const string PermissionWhView = "WH_VIEW";
        private const string PermissionRptView = "RPT_VIEW";
        private const string PermissionAuditView = "AUDIT_VIEW";
        private static bool IsSqlServer(ApplicationDbContext context)
        {
            return context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
        }

        public static void Initialize(ApplicationDbContext context, IConfiguration configuration, ILogger? logger = null)
        {
            // Ensure the database and schema exist without deleting existing data
            logger?.LogInformation("DbInitializer: Calling EnsureCreated...");
            _ = context.Database.EnsureCreated();

            var superAdminEmail = ResolveSuperAdminEmail(configuration, logger);
            var sampleApiBaseUrl = configuration["SeedSettings:SampleApiBaseUrl"]?.Trim();

            EnsureWarehouseTable(context, logger);
            EnsureSubmissionNewColumns(context, logger);
            EnsureNewTables(context, logger);
            EnsureRoleNewColumns(context, logger);
            EnsureOrganizationNewColumns(context, logger);
            EnsureUserNewColumns(context, logger);

            SeedRoles(context);
            SeedPermissions(context);
            SeedRolePermissions(context);
            SeedOrganizations(context);
            SeedUsers(context, superAdminEmail, logger);
            SeedDataSources(context, sampleApiBaseUrl, logger);
            SeedDataCleansingRules(context);
            SeedSystemConfigurations(context);
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

        private static void SeedRoles(ApplicationDbContext context)
        {
            if (context.Roles.Any())
            {
                return;
            }

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

        private static void SeedPermissions(ApplicationDbContext context)
        {
            if (context.Permissions.Any())
            {
                return;
            }

            var permissions = new Permission[]
            {
                new() { PermissionCode = PermissionDashView, PermissionName = "View Dashboard", ModuleName = "Dashboard", Description = "Access the dashboard overview" },
                new() { PermissionCode = PermissionSrcView, PermissionName = "View Sources", ModuleName = ModuleDataSources, Description = "View API data sources" },
                new() { PermissionCode = "SRC_CREATE", PermissionName = "Create Source", ModuleName = ModuleDataSources, Description = "Add new API data sources" },
                new() { PermissionCode = "SRC_DELETE", PermissionName = "Delete Source", ModuleName = ModuleDataSources, Description = "Remove API data sources" },
                new() { PermissionCode = "SRC_TOGGLE", PermissionName = "Toggle Source Status", ModuleName = ModuleDataSources, Description = "Activate/deactivate data sources" },
                new() { PermissionCode = "DATA_PULL", PermissionName = "Pull Data", ModuleName = ModuleDataUpload, Description = "Pull data from API sources into staging" },
                new() { PermissionCode = "DATA_VALIDATE", PermissionName = "Validate Data", ModuleName = ModuleDataUpload, Description = "Validate staging records" },
                new() { PermissionCode = "DATA_CORRECT", PermissionName = "Correct Records", ModuleName = ModuleDataUpload, Description = "Correct flagged staging records" },
                new() { PermissionCode = "DATA_SUBMIT", PermissionName = "Submit Batch", ModuleName = ModuleDataUpload, Description = "Submit validated batches for approval" },
                new() { PermissionCode = "SUB_APPROVE", PermissionName = "Approve Submission", ModuleName = "Approvals", Description = "Approve and load submissions into warehouse" },
                new() { PermissionCode = "SUB_REJECT", PermissionName = "Reject Submission", ModuleName = "Approvals", Description = "Reject submissions" },
                new() { PermissionCode = PermissionWhView, PermissionName = "View Warehouse", ModuleName = "Warehouse", Description = "Browse warehouse data" },
                new() { PermissionCode = "WH_ARCHIVE", PermissionName = "Archive Table", ModuleName = "Warehouse", Description = "Archive warehouse table data" },
                new() { PermissionCode = "WH_RESTORE", PermissionName = "Restore Table", ModuleName = "Warehouse", Description = "Restore archived warehouse data" },
                new() { PermissionCode = PermissionRptView, PermissionName = "View Reports", ModuleName = "Reports", Description = "Access reports and analytics" },
                new() { PermissionCode = PermissionAuditView, PermissionName = "View Audit Logs", ModuleName = "AuditTrail", Description = "View audit and activity logs" },
                new() { PermissionCode = "USR_VIEW", PermissionName = "View Users", ModuleName = "UserManagement", Description = "View organization users" },
                new() { PermissionCode = "USR_CREATE", PermissionName = "Create User", ModuleName = "UserManagement", Description = "Add new users to organization" },
                new() { PermissionCode = "USR_MANAGE", PermissionName = "Manage Users", ModuleName = "UserManagement", Description = "Edit/disable users across all organizations" },
                new() { PermissionCode = "SUB_MANAGE", PermissionName = "Manage Subscription", ModuleName = "Subscription", Description = "Upgrade/manage subscription plans" },
                new() { PermissionCode = "SYS_ADMIN", PermissionName = "System Administration", ModuleName = "System", Description = "Full system-wide administration" },
                new() { PermissionCode = "CLNS_VIEW", PermissionName = "View Cleansing", ModuleName = "DataCleansing", Description = "View data quality and cleansing results" },
                new() { PermissionCode = "SEC_VIEW", PermissionName = "View Security", ModuleName = "Security", Description = "View security audit and user access logs" }
            };
            context.Permissions.AddRange(permissions);
            context.SaveChanges();
        }

        private static void SeedRolePermissions(ApplicationDbContext context)
        {
            if (context.RolePermissions.Any())
            {
                return;
            }

            var allPerms = context.Permissions.ToList();
            var roles = context.Roles.ToList();

            var superAdmin = roles.First(r => r.RoleName == "SuperAdmin");
            var userAdmin = roles.First(r => r.RoleName == "UserAdmin");
            var analyst = roles.First(r => r.RoleName == "DataAnalyst");
            var manager = roles.First(r => r.RoleName == "Manager");
            var staff = roles.First(r => r.RoleName == "Staff");

            var rolePermissions = new List<RolePermission>();

            foreach (var p in allPerms)
            {
                rolePermissions.Add(new() { RoleID = superAdmin.RoleID, PermissionID = p.PermissionID });
            }

            var userAdminCodes = new[] { PermissionDashView, PermissionSrcView, "SRC_TOGGLE", PermissionWhView, "WH_ARCHIVE", "WH_RESTORE", PermissionRptView, PermissionAuditView, "USR_VIEW", "USR_CREATE", "SUB_MANAGE", "CLNS_VIEW" };
            AddRolePermissions(rolePermissions, userAdmin.RoleID, allPerms, userAdminCodes);

            var analystCodes = new[] { PermissionDashView, PermissionSrcView, PermissionWhView, PermissionRptView, PermissionAuditView, "CLNS_VIEW", "SEC_VIEW" };
            AddRolePermissions(rolePermissions, analyst.RoleID, allPerms, analystCodes);

            var managerCodes = new[] { PermissionDashView, PermissionSrcView, PermissionWhView, PermissionRptView, PermissionAuditView, "SUB_APPROVE", "SUB_REJECT" };
            AddRolePermissions(rolePermissions, manager.RoleID, allPerms, managerCodes);

            var staffCodes = new[] { PermissionDashView, PermissionSrcView, "SRC_CREATE", "SRC_DELETE", "DATA_PULL", "DATA_VALIDATE", "DATA_CORRECT", "DATA_SUBMIT", PermissionRptView, PermissionAuditView };
            AddRolePermissions(rolePermissions, staff.RoleID, allPerms, staffCodes);

            context.RolePermissions.AddRange(rolePermissions);
            context.SaveChanges();
        }

        private static void AddRolePermissions(List<RolePermission> rolePermissions, int roleId, List<Permission> permissions, IEnumerable<string> codes)
        {
            foreach (var code in codes)
            {
                var permission = permissions.FirstOrDefault(x => x.PermissionCode == code);
                if (permission != null)
                {
                    rolePermissions.Add(new RolePermission { RoleID = roleId, PermissionID = permission.PermissionID });
                }
            }
        }

        private static void SeedOrganizations(ApplicationDbContext context)
        {
            if (context.Organizations.Any())
            {
                return;
            }

            var orgs = new Organization[]
            {
                new() { OrganizationName = "Neyo System" },
                new() { OrganizationName = "Default Org" }
            };
            context.Organizations.AddRange(orgs);
            context.SaveChanges();
        }

        private static void SeedUsers(ApplicationDbContext context, string superAdminEmail, ILogger? logger)
        {
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
                        Account_status = AccountStatusActive,
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
                        Account_status = AccountStatusActive,
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
                        Account_status = AccountStatusActive,
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
                        Account_status = AccountStatusActive,
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
                        Account_status = AccountStatusActive,
                        Last_login = null,
                        Created_at = DateTime.UtcNow
                    }
                };

                context.Users.AddRange(users);
                context.SaveChanges();
                logger?.LogInformation("DbInitializer: Users seeded successfully. Count = {Count}", context.Users.Count());
                return;
            }

            var existingSuperAdmin = context.Users.FirstOrDefault(u => u.Username == "superadmin");
            if (existingSuperAdmin != null && !IsValidEmail(existingSuperAdmin.Email))
            {
                logger?.LogWarning("DbInitializer: SuperAdmin email is invalid. Updating to configured seed email.");
                existingSuperAdmin.Email = superAdminEmail;
                context.SaveChanges();
            }

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

                bool passwordValid;
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
                    user.Account_status = AccountStatusActive;
                    context.SaveChanges();
                    logger?.LogInformation("DbInitializer: Password reset successfully for '{Username}'.", username);
                }
            }
        }

        private static void SeedDataSources(ApplicationDbContext context, string? sampleApiBaseUrl, ILogger? logger)
        {
            if (context.DataSources.Any())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(sampleApiBaseUrl))
            {
                logger?.LogWarning("DbInitializer: Sample API base URL not configured. Skipping sample data sources.");
                return;
            }

            var staffUser = context.Users.First(u => u.Username == "staff");
            var normalizedBaseUrl = sampleApiBaseUrl.TrimEnd('/');

            var dataSources = new DataSource[]
            {
                CreateSampleSource("JSONPlaceholder - Users", normalizedBaseUrl, "/users", "stg_crm_contacts", staffUser),
                CreateSampleSource("JSONPlaceholder - Posts", normalizedBaseUrl, "/posts", "stg_content_feed", staffUser),
                CreateSampleSource("JSONPlaceholder - Comments", normalizedBaseUrl, "/comments", "stg_feedback", staffUser),
                CreateSampleSource("JSONPlaceholder - Todos", normalizedBaseUrl, "/todos", "stg_task_tracker", staffUser),
                CreateSampleSource("JSONPlaceholder - Albums", normalizedBaseUrl, "/albums", "stg_media_catalog", staffUser)
            };

            context.DataSources.AddRange(dataSources);
            context.SaveChanges();
        }

        private static DataSource CreateSampleSource(string name, string baseUrl, string endpoint, string targetTable, User staffUser)
        {
            return new DataSource
            {
                SourceName = name,
                ApiBaseUrl = baseUrl,
                ApiEndpoint = endpoint,
                ApiKey = string.Empty,
                AuthMethod = "None",
                TargetTable = targetTable,
                Status = AccountStatusActive,
                CreatedByUserID = staffUser.UserID,
                OrganizationID = staffUser.OrganizationID,
                Created_at = DateTime.UtcNow
            };
        }

        private static void SeedDataCleansingRules(ApplicationDbContext context)
        {
            if (context.DataCleansingRules.Any())
            {
                return;
            }

            var rules = new DataCleansingRule[]
            {
                new() { RuleName = "Trim Whitespace", RuleType = "TrimWhitespace", RuleDescription = "Remove leading and trailing whitespace from string values" },
                new() { RuleName = "Remove Nulls", RuleType = "RemoveNulls", RuleDescription = "Remove fields with null values from records" },
                new() { RuleName = "Remove Empty Strings", RuleType = "RemoveEmpty", RuleDescription = "Remove fields with empty string values" }
            };
            context.DataCleansingRules.AddRange(rules);
            context.SaveChanges();
        }

        private static void SeedSystemConfigurations(ApplicationDbContext context)
        {
            if (context.SystemConfigurations.Any())
            {
                return;
            }

            var configs = new SystemConfiguration[]
            {
                new() { ConfigKey = "MaxApiTimeout", ConfigValue = "30", Description = "Maximum API request timeout in seconds" },
                new() { ConfigKey = "MaxStagingRowsPerBatch", ConfigValue = "10000", Description = "Maximum rows allowed per staging batch" },
                new() { ConfigKey = "DefaultLoadMode", ConfigValue = "Append", Description = "Default load mode for warehouse integration" },
                new() { ConfigKey = "HistoricalRetentionDays", ConfigValue = "365", Description = "Number of days to retain historical data snapshots" },
                new() { ConfigKey = "AuditLogRetentionDays", ConfigValue = "730", Description = "Number of days to retain audit logs" },
                new() { ConfigKey = "SuperAdminLockdownEnabled", ConfigValue = "false", Description = "Block SuperAdmin access during incident response" },
                new() { ConfigKey = "SuperAdminLockdownReason", ConfigValue = "", Description = "Reason for SuperAdmin lockdown" }
            };
            context.SystemConfigurations.AddRange(configs);
            context.SaveChanges();
        }

        private static bool IsValidEmail(string? email)
        {
            return MailAddress.TryCreate(email, out _);
        }

        private static void EnsureWarehouseTable(ApplicationDbContext context, ILogger? logger)
        {
            try
            {
                context.WarehouseRecords.Any();
            }
            catch (Exception ex)
            {
                logger?.LogInformation(ex, "DbInitializer: Creating WarehouseRecords table.");
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

        private static void EnsureSubmissionNewColumns(ApplicationDbContext context, ILogger? logger)
        {
            if (IsSqlServer(context))
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DataSubmissions') AND name = 'Integrated_at')
                        ALTER TABLE DataSubmissions ADD Integrated_at DATETIME2 NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DataSubmissions') AND name = 'LoadedRows')
                        ALTER TABLE DataSubmissions ADD LoadedRows INT NOT NULL DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DataSubmissions') AND name = 'SkippedRows')
                        ALTER TABLE DataSubmissions ADD SkippedRows INT NOT NULL DEFAULT 0;"),
                    "Ensure DataSubmissions columns");
            }
            else
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE DataSubmissions ADD COLUMN Integrated_at TEXT NULL"),
                    "Ensure DataSubmissions.Integrated_at (SQLite)");
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE DataSubmissions ADD COLUMN LoadedRows INTEGER NOT NULL DEFAULT 0"),
                    "Ensure DataSubmissions.LoadedRows (SQLite)");
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE DataSubmissions ADD COLUMN SkippedRows INTEGER NOT NULL DEFAULT 0"),
                    "Ensure DataSubmissions.SkippedRows (SQLite)");
            }
        }

        /// <summary>
        /// Creates the 9 new tables added to the detailed database design.
        /// </summary>
        private static void EnsureNewTables(ApplicationDbContext context, ILogger? logger)
        {
            if (IsSqlServer(context))
            {
                EnsureTableExists(logger, "Permissions", () => context.Permissions.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Permissions' AND xtype='U') CREATE TABLE Permissions (PermissionID INT IDENTITY(1,1) PRIMARY KEY, PermissionCode NVARCHAR(50) NOT NULL, PermissionName NVARCHAR(100) NOT NULL, ModuleName NVARCHAR(100) NOT NULL, Description NVARCHAR(300) NULL)"));

                EnsureTableExists(logger, "RolePermissions", () => context.RolePermissions.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RolePermissions' AND xtype='U') CREATE TABLE RolePermissions (RolePermissionID INT IDENTITY(1,1) PRIMARY KEY, RoleID INT NOT NULL, PermissionID INT NOT NULL, CONSTRAINT FK_RolePermissions_Role FOREIGN KEY (RoleID) REFERENCES Roles(RoleID) ON DELETE CASCADE, CONSTRAINT FK_RolePermissions_Permission FOREIGN KEY (PermissionID) REFERENCES Permissions(PermissionID) ON DELETE CASCADE)"));

                EnsureTableExists(logger, "DataSourceFields", () => context.DataSourceFields.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataSourceFields' AND xtype='U') CREATE TABLE DataSourceFields (FieldID INT IDENTITY(1,1) PRIMARY KEY, DataSourceID INT NOT NULL, FieldName NVARCHAR(100) NOT NULL, DataType NVARCHAR(50) NOT NULL DEFAULT 'string', FieldLength INT NULL, IsRequired BIT NOT NULL DEFAULT 0, CONSTRAINT FK_DataSourceFields_DataSource FOREIGN KEY (DataSourceID) REFERENCES DataSources(DataSourceID) ON DELETE CASCADE)"));

                EnsureTableExists(logger, "UploadErrorLogs", () => context.UploadErrorLogs.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UploadErrorLogs' AND xtype='U') CREATE TABLE UploadErrorLogs (ErrorID INT IDENTITY(1,1) PRIMARY KEY, StagingRecordID INT NOT NULL, ErrorDescription NVARCHAR(500) NOT NULL, Error_timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_UploadErrorLogs_StagingRecord FOREIGN KEY (StagingRecordID) REFERENCES StagingRecords(StagingRecordID) ON DELETE CASCADE)"));

                EnsureTableExists(logger, "ETLStageLogs", () => context.ETLStageLogs.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ETLStageLogs' AND xtype='U') CREATE TABLE ETLStageLogs (StageLogID INT IDENTITY(1,1) PRIMARY KEY, SubmissionID INT NOT NULL, StageName NVARCHAR(50) NOT NULL, Status NVARCHAR(20) NOT NULL DEFAULT 'Running', Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_ETLStageLogs_Submission FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID) ON DELETE CASCADE)"));

                EnsureTableExists(logger, "DataCleansingRules", () => context.DataCleansingRules.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataCleansingRules' AND xtype='U') CREATE TABLE DataCleansingRules (RuleID INT IDENTITY(1,1) PRIMARY KEY, RuleName NVARCHAR(100) NOT NULL, RuleType NVARCHAR(50) NOT NULL, RuleDescription NVARCHAR(500) NULL)"));

                EnsureTableExists(logger, "DataCleansingLogs", () => context.DataCleansingLogs.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DataCleansingLogs' AND xtype='U') CREATE TABLE DataCleansingLogs (CleansingLogID INT IDENTITY(1,1) PRIMARY KEY, SubmissionID INT NOT NULL, RuleID INT NOT NULL, AffectedRecords INT NOT NULL DEFAULT 0, CorrectionType NVARCHAR(50) NOT NULL, Cleansing_date DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_DataCleansingLogs_Submission FOREIGN KEY (SubmissionID) REFERENCES DataSubmissions(SubmissionID), CONSTRAINT FK_DataCleansingLogs_Rule FOREIGN KEY (RuleID) REFERENCES DataCleansingRules(RuleID))"));

                EnsureTableExists(logger, "HistoricalData", () => context.HistoricalData.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='HistoricalData' AND xtype='U') CREATE TABLE HistoricalData (HistoricalDataID INT IDENTITY(1,1) PRIMARY KEY, WarehouseRecordID INT NOT NULL, VersionNo INT NOT NULL, Snapshot_date DATETIME2 NOT NULL DEFAULT GETUTCDATE(), DataPayload NVARCHAR(MAX) NOT NULL, Retention_until DATETIME2 NULL, CONSTRAINT FK_HistoricalData_WarehouseRecord FOREIGN KEY (WarehouseRecordID) REFERENCES WarehouseRecords(WarehouseRecordID) ON DELETE CASCADE)"));

                EnsureTableExists(logger, "SystemLogs", () => context.SystemLogs.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemLogs' AND xtype='U') CREATE TABLE SystemLogs (LogID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NOT NULL, Module NVARCHAR(100) NOT NULL, Action NVARCHAR(100) NOT NULL, Action_timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_SystemLogs_User FOREIGN KEY (UserID) REFERENCES Users(UserID))"));

                EnsureTableExists(logger, "SecurityLogs", () => context.SecurityLogs.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SecurityLogs' AND xtype='U') CREATE TABLE SecurityLogs (SecurityLogID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NULL, EventType NVARCHAR(100) NOT NULL, Details NVARCHAR(1000) NULL, IpAddress NVARCHAR(100) NULL, Created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_SecurityLogs_User FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE SET NULL)"));

                EnsureTableExists(logger, "SystemConfigurations", () => context.SystemConfigurations.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemConfigurations' AND xtype='U') CREATE TABLE SystemConfigurations (ConfigID INT IDENTITY(1,1) PRIMARY KEY, ConfigKey NVARCHAR(100) NOT NULL, ConfigValue NVARCHAR(500) NOT NULL, Description NVARCHAR(300) NULL, Updated_at DATETIME2 NOT NULL DEFAULT GETUTCDATE())"));

                EnsureTableExists(logger, "Notifications", () => context.Notifications.Any(),
                    () => context.Database.ExecuteSqlRaw(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Notifications' AND xtype='U') CREATE TABLE Notifications (NotificationID INT IDENTITY(1,1) PRIMARY KEY, UserID INT NOT NULL, OrganizationID INT NOT NULL, Title NVARCHAR(200) NOT NULL, Message NVARCHAR(500) NULL, Type NVARCHAR(50) NOT NULL DEFAULT 'Info', Link NVARCHAR(500) NULL, IsRead BIT NOT NULL DEFAULT 0, Created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(), CONSTRAINT FK_Notifications_User FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE CASCADE, CONSTRAINT FK_Notifications_Org FOREIGN KEY (OrganizationID) REFERENCES Organizations(OrganizationID))"));
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

        private static void EnsureRoleNewColumns(ApplicationDbContext context, ILogger? logger)
        {
            if (IsSqlServer(context))
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Roles') AND name = 'Description')
                        ALTER TABLE Roles ADD Description NVARCHAR(200) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Roles') AND name = 'RoleLevel')
                        ALTER TABLE Roles ADD RoleLevel INT NOT NULL DEFAULT 0;"),
                    "Ensure Roles columns");
            }
            else
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Roles ADD COLUMN Description TEXT NULL"),
                    "Ensure Roles.Description (SQLite)");
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Roles ADD COLUMN RoleLevel INTEGER NOT NULL DEFAULT 0"),
                    "Ensure Roles.RoleLevel (SQLite)");
            }
        }

        private static void EnsureOrganizationNewColumns(ApplicationDbContext context, ILogger? logger)
        {
            if (IsSqlServer(context))
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Organizations') AND name = 'Created_at')
                        ALTER TABLE Organizations ADD Created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE();"),
                    "Ensure Organizations.Created_at");
            }
            else
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Organizations ADD COLUMN Created_at TEXT NOT NULL DEFAULT (datetime('now'))"),
                    "Ensure Organizations.Created_at (SQLite)");
            }
        }

        private static void EnsureUserNewColumns(ApplicationDbContext context, ILogger? logger)
        {
            if (IsSqlServer(context))
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw(@"
                        IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Email')
                        ALTER TABLE Users ALTER COLUMN Email NVARCHAR(100) NOT NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'FailedLoginAttempts')
                        ALTER TABLE Users ADD FailedLoginAttempts INT NOT NULL DEFAULT 0;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'LockoutUntil')
                        ALTER TABLE Users ADD LockoutUntil DATETIME2 NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'TwoFactorCodeHash')
                        ALTER TABLE Users ADD TwoFactorCodeHash NVARCHAR(200) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'TwoFactorCodeExpiresAt')
                        ALTER TABLE Users ADD TwoFactorCodeExpiresAt DATETIME2 NULL;"),
                    "Ensure Users columns");
            }
            else
            {
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN FailedLoginAttempts INTEGER NOT NULL DEFAULT 0"),
                    "Ensure Users.FailedLoginAttempts (SQLite)");
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN LockoutUntil TEXT NULL"),
                    "Ensure Users.LockoutUntil (SQLite)");
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN TwoFactorCodeHash TEXT NULL"),
                    "Ensure Users.TwoFactorCodeHash (SQLite)");
                TryExecute(logger, () => context.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN TwoFactorCodeExpiresAt TEXT NULL"),
                    "Ensure Users.TwoFactorCodeExpiresAt (SQLite)");
            }
        }

        private static void EnsureTableExists(ILogger? logger, string tableName, Func<bool> tableCheck, Action createTable)
        {
            var tableExists = false;

            try
            {
                _ = tableCheck();
                tableExists = true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "DbInitializer: Failed to validate table {TableName}. Will attempt to create it.", tableName);
            }

            if (tableExists)
            {
                return;
            }

            TryExecute(logger, createTable, $"Ensure {tableName} table");
        }

        private static void TryExecute(ILogger? logger, Action action, string description)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "DbInitializer: {Description} failed.", description);
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
