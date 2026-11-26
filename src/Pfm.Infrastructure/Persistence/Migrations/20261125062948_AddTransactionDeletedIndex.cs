using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionDeletedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_Deleted",
                table: "Transactions",
                columns: new[] { "UserId", "DeletedAt" },
                filter: "\"DeletedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_Deleted",
                table: "Transactions");
        }
    }
}
