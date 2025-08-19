using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PowerDaemon.Central.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    OsType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AgentStatus = table.Column<string>(type: "text", nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CpuCores = table.Column<int>(type: "integer", nullable: true),
                    TotalMemoryMb = table.Column<int>(type: "integer", nullable: true),
                    Location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConnectionString = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DeploymentScript = table.Column<string>(type: "text", nullable: true),
                    HealthCheckTemplate = table.Column<string>(type: "text", nullable: true),
                    DefaultPortRange = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ConfigurationTemplate = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProcessId = table.Column<int>(type: "integer", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    ExecutablePath = table.Column<string>(type: "text", nullable: false),
                    WorkingDirectory = table.Column<string>(type: "text", nullable: true),
                    ConfigFilePath = table.Column<string>(type: "text", nullable: true),
                    StartupType = table.Column<string>(type: "text", nullable: false),
                    ServiceAccount = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastStartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HealthCheckUrl = table.Column<string>(type: "text", nullable: true),
                    Dependencies = table.Column<string>(type: "text", nullable: true),
                    CustomMetrics = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Services_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PackagePath = table.Column<string>(type: "text", nullable: false),
                    PackageSizeMb = table.Column<decimal>(type: "numeric", nullable: true),
                    PackageChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DeploymentStrategy = table.Column<string>(type: "text", nullable: false),
                    DeployedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DeploymentNotes = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    PreviousVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RollbackDeploymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigurationChanges = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_Deployments_RollbackDeploymentId",
                        column: x => x.RollbackDeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Deployments_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetricType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Metrics_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Metrics_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ServiceTypes",
                columns: new[] { "Id", "ConfigurationTemplate", "CreatedAt", "DefaultPortRange", "DeploymentScript", "Description", "HealthCheckTemplate", "IsActive", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), null, new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2690), "8000-8099", null, "Service Type A", null, true, "TypeA", new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2680) },
                    { new Guid("22222222-2222-2222-2222-222222222222"), null, new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2700), "8100-8199", null, "Service Type B", null, true, "TypeB", new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2700) },
                    { new Guid("33333333-3333-3333-3333-333333333333"), null, new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2700), "8200-8299", null, "Service Type C", null, true, "TypeC", new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2700) },
                    { new Guid("44444444-4444-4444-4444-444444444444"), null, new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2700), "8300-8399", null, "Service Type D", null, true, "TypeD", new DateTime(2025, 8, 19, 15, 15, 35, 123, DateTimeKind.Utc).AddTicks(2700) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_CreatedAt",
                table: "Deployments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_RollbackDeploymentId",
                table: "Deployments",
                column: "RollbackDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ServiceId",
                table: "Deployments",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ServiceId_Version",
                table: "Deployments",
                columns: new[] { "ServiceId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Status",
                table: "Deployments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Metrics_MetricType_MetricName_Timestamp",
                table: "Metrics",
                columns: new[] { "MetricType", "MetricName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Metrics_ServerId_Timestamp",
                table: "Metrics",
                columns: new[] { "ServerId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Metrics_ServiceId_Timestamp",
                table: "Metrics",
                columns: new[] { "ServiceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Servers_AgentStatus",
                table: "Servers",
                column: "AgentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_Hostname",
                table: "Servers",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_LastHeartbeat",
                table: "Servers",
                column: "LastHeartbeat");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_OsType",
                table: "Servers",
                column: "OsType");

            migrationBuilder.CreateIndex(
                name: "IX_Services_IsActive",
                table: "Services",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ServerId_Name",
                table: "Services",
                columns: new[] { "ServerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Services_ServiceTypeId",
                table: "Services",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_Status",
                table: "Services",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_Name",
                table: "ServiceTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Metrics");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "ServiceTypes");
        }
    }
}
