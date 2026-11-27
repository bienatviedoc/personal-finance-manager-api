using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatchRowCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RowCategories",
                table: "ImportBatches",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowCategories",
                table: "ImportBatches");
        }
    }
}
