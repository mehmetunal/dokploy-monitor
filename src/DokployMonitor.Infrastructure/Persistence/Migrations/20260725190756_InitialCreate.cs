using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DokployMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    DeploymentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LogPath = table.Column<string>(type: "TEXT", nullable: true),
                    Pid = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicationId = table.Column<string>(type: "TEXT", nullable: true),
                    ComposeId = table.Column<string>(type: "TEXT", nullable: true),
                    ServerId = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduleId = table.Column<string>(type: "TEXT", nullable: true),
                    BackupId = table.Column<string>(type: "TEXT", nullable: true),
                    VolumeBackupId = table.Column<string>(type: "TEXT", nullable: true),
                    PreviewDeploymentId = table.Column<string>(type: "TEXT", nullable: true),
                    IsPreviewDeployment = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ServiceId = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: true),
                    AppName = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: true),
                    EnvironmentId = table.Column<string>(type: "TEXT", nullable: true),
                    EnvironmentName = table.Column<string>(type: "TEXT", nullable: true),
                    ServerName = table.Column<string>(type: "TEXT", nullable: true),
                    BuildServerName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorSignatureHash = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FirstSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedLogPath = table.Column<string>(type: "TEXT", nullable: true),
                    RawJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.DeploymentId);
                });

            migrationBuilder.CreateTable(
                name: "ErrorSignatures",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NormalizedMessage = table.Column<string>(type: "TEXT", nullable: false),
                    SampleMessage = table.Column<string>(type: "TEXT", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastServiceName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorSignatures", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "WebhookNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceivedAt = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicationName = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicationType = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    BuildLink = table.Column<string>(type: "TEXT", nullable: true),
                    Domains = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceId = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    RawJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeploymentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentEvents_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "DeploymentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEvents_DeploymentId",
                table: "DeploymentEvents",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEvents_OccurredAt",
                table: "DeploymentEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_CreatedAt",
                table: "Deployments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ErrorSignatureHash",
                table: "Deployments",
                column: "ErrorSignatureHash");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ProjectId",
                table: "Deployments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ServiceId",
                table: "Deployments",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Status",
                table: "Deployments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorSignatures_LastSeenAt",
                table: "ErrorSignatures",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookNotifications_ReceivedAt",
                table: "WebhookNotifications",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentEvents");

            migrationBuilder.DropTable(
                name: "ErrorSignatures");

            migrationBuilder.DropTable(
                name: "WebhookNotifications");

            migrationBuilder.DropTable(
                name: "Deployments");
        }
    }
}
