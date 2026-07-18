using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractionSourceMetadataToExtractedField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractionMethod",
                table: "ExtractedFields",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceText",
                table: "ExtractedFields",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractionMethod",
                table: "ExtractedFields");

            migrationBuilder.DropColumn(
                name: "SourceText",
                table: "ExtractedFields");
        }
    }
}
