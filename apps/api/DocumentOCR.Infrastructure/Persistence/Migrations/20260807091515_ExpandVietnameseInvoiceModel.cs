using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandVietnameseInvoiceModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue must be a valid DocumentDirection string (not "") — existing rows get
            // backfilled with this value, and HasConversion<string>() would throw materializing
            // an empty string back into the enum on next read.
            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "Documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateTable(
                name: "InvoiceTaxBreakdowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawVatRate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VatRate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TaxableAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTaxBreakdowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceTaxBreakdowns_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTaxBreakdowns_DocumentId_SortOrder",
                table: "InvoiceTaxBreakdowns",
                columns: new[] { "DocumentId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceTaxBreakdowns");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "Documents");
        }
    }
}
