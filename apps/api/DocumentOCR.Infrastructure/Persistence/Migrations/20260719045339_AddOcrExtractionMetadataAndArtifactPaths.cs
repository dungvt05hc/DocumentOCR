using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrExtractionMetadataAndArtifactPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedResultPath",
                table: "OcrProviderLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawResponsePath",
                table: "OcrProviderLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderFieldName",
                table: "ExtractedFields",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "ExtractedFields",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizedResultPath",
                table: "OcrProviderLogs");

            migrationBuilder.DropColumn(
                name: "RawResponsePath",
                table: "OcrProviderLogs");

            migrationBuilder.DropColumn(
                name: "ProviderFieldName",
                table: "ExtractedFields");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "ExtractedFields");
        }
    }
}
