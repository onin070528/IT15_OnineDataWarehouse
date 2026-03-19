using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace it15_webproject_mvc.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrganizationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationID);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleID);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationID = table.Column<int>(type: "int", nullable: false),
                    RoleID = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Full_name = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Account_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Last_login = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserID);
                    table.ForeignKey(
                        name: "FK_Users_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleID",
                        column: x => x.RoleID,
                        principalTable: "Roles",
                        principalColumn: "RoleID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    EntityName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PerformedByUserID = table.Column<int>(type: "int", nullable: false),
                    OrganizationID = table.Column<int>(type: "int", nullable: false),
                    Performed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogID);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_PerformedByUserID",
                        column: x => x.PerformedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    DataSourceID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApiEndpoint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AuthMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Last_sync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: false),
                    OrganizationID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.DataSourceID);
                    table.ForeignKey(
                        name: "FK_DataSources_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DataSources_Users_CreatedByUserID",
                        column: x => x.CreatedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    PaymentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    OrganizationID = table.Column<int>(type: "int", nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CheckoutSessionId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PayMongoPaymentId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Paid_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.PaymentID);
                    table.ForeignKey(
                        name: "FK_Payments_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataSubmissions",
                columns: table => new
                {
                    SubmissionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataSourceID = table.Column<int>(type: "int", nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ValidRows = table.Column<int>(type: "int", nullable: false),
                    ErrorRows = table.Column<int>(type: "int", nullable: false),
                    LoadMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedByUserID = table.Column<int>(type: "int", nullable: false),
                    OrganizationID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSubmissions", x => x.SubmissionID);
                    table.ForeignKey(
                        name: "FK_DataSubmissions_DataSources_DataSourceID",
                        column: x => x.DataSourceID,
                        principalTable: "DataSources",
                        principalColumn: "DataSourceID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DataSubmissions_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DataSubmissions_Users_SubmittedByUserID",
                        column: x => x.SubmittedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StagingRecords",
                columns: table => new
                {
                    StagingRecordID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataSourceID = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RawData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Pulled_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PulledByUserID = table.Column<int>(type: "int", nullable: false),
                    SubmissionID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingRecords", x => x.StagingRecordID);
                    table.ForeignKey(
                        name: "FK_StagingRecords_DataSources_DataSourceID",
                        column: x => x.DataSourceID,
                        principalTable: "DataSources",
                        principalColumn: "DataSourceID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StagingRecords_DataSubmissions_SubmissionID",
                        column: x => x.SubmissionID,
                        principalTable: "DataSubmissions",
                        principalColumn: "SubmissionID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StagingRecords_Users_PulledByUserID",
                        column: x => x.PulledByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationID",
                table: "AuditLogs",
                column: "OrganizationID");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_PerformedByUserID",
                table: "AuditLogs",
                column: "PerformedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_CreatedByUserID",
                table: "DataSources",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_OrganizationID",
                table: "DataSources",
                column: "OrganizationID");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubmissions_DataSourceID",
                table: "DataSubmissions",
                column: "DataSourceID");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubmissions_OrganizationID",
                table: "DataSubmissions",
                column: "OrganizationID");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubmissions_SubmittedByUserID",
                table: "DataSubmissions",
                column: "SubmittedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationID",
                table: "Payments",
                column: "OrganizationID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserID",
                table: "Payments",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_StagingRecords_DataSourceID",
                table: "StagingRecords",
                column: "DataSourceID");

            migrationBuilder.CreateIndex(
                name: "IX_StagingRecords_PulledByUserID",
                table: "StagingRecords",
                column: "PulledByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_StagingRecords_SubmissionID",
                table: "StagingRecords",
                column: "SubmissionID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationID",
                table: "Users",
                column: "OrganizationID");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleID",
                table: "Users",
                column: "RoleID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "StagingRecords");

            migrationBuilder.DropTable(
                name: "DataSubmissions");

            migrationBuilder.DropTable(
                name: "DataSources");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
