using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_OrganizationId",
                table: "Documents");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientProfileId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientProfiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ClientProfileId",
                table: "Documents",
                column: "ClientProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId_ClientProfileId_CreatedAt",
                table: "Documents",
                columns: new[] { "OrganizationId", "ClientProfileId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientProfiles_OrganizationId_TaxCode",
                table: "ClientProfiles",
                columns: new[] { "OrganizationId", "TaxCode" },
                unique: true,
                filter: "\"TaxCode\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_ClientProfiles_ClientProfileId",
                table: "Documents",
                column: "ClientProfileId",
                principalTable: "ClientProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_ClientProfiles_ClientProfileId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "ClientProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ClientProfileId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_OrganizationId_ClientProfileId_CreatedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ClientProfileId",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId",
                table: "Documents",
                column: "OrganizationId");
        }
    }
}
