using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelIdAndRawResponseJsonToOcrProviderLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "OcrProviderLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawResponseJson",
                table: "OcrProviderLogs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "OcrProviderLogs");

            migrationBuilder.DropColumn(
                name: "RawResponseJson",
                table: "OcrProviderLogs");
        }
    }
}
