using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAppUserExportJobUsageLog_MakeOcrProviderLogAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "ExportJobDocuments");

            migrationBuilder.DropTable(
                name: "UsageLogs");

            migrationBuilder.DropTable(
                name: "ExportJobs");

            migrationBuilder.DropIndex(
                name: "IX_OcrProviderLogs_DocumentId",
                table: "OcrProviderLogs");

            migrationBuilder.CreateIndex(
                name: "IX_OcrProviderLogs_CreatedAt",
                table: "OcrProviderLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OcrProviderLogs_DocumentId",
                table: "OcrProviderLogs",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OcrProviderLogs_CreatedAt",
                table: "OcrProviderLogs");

            migrationBuilder.DropIndex(
                name: "IX_OcrProviderLogs_DocumentId",
                table: "OcrProviderLogs");

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUsers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoredFilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessingDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExportJobDocuments",
                columns: table => new
                {
                    DocumentsId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExportJobsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobDocuments", x => new { x.DocumentsId, x.ExportJobsId });
                    table.ForeignKey(
                        name: "FK_ExportJobDocuments_Documents_DocumentsId",
                        column: x => x.DocumentsId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExportJobDocuments_ExportJobs_ExportJobsId",
                        column: x => x.ExportJobsId,
                        principalTable: "ExportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OcrProviderLogs_DocumentId",
                table: "OcrProviderLogs",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Email",
                table: "AppUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_OrganizationId",
                table: "AppUsers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobDocuments_ExportJobsId",
                table: "ExportJobDocuments",
                column: "ExportJobsId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLogs_CreatedAt",
                table: "UsageLogs",
                column: "CreatedAt");
        }
    }
}
