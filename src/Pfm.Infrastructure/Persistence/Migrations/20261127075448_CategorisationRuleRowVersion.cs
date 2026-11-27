using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategorisationRuleRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "CategorisationRules",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "CategorisationRules");
        }
    }
}
