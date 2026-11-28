using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The index of a category's lines carries each line's amount and position, so the sums of a category's lines (its
    /// transaction list's totals and spent, its reports) read them from the index without visiting the table.
    /// </summary>
    public partial class CoverCategoryLinesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransactionLines_CategoryId_TransactionId",
                table: "TransactionLines");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_CategoryId_TransactionId",
                table: "TransactionLines",
                columns: new[] { "CategoryId", "TransactionId" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransactionLines_CategoryId_TransactionId",
                table: "TransactionLines");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_CategoryId_TransactionId",
                table: "TransactionLines",
                columns: new[] { "CategoryId", "TransactionId" });
        }
    }
}
