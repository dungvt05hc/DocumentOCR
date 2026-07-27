using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentOCR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTablesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TablesJson",
                table: "Documents",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TablesJson",
                table: "Documents");
        }
    }
}
