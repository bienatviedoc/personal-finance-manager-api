using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionCategoryKindReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryKind",
                table: "Transactions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "CASE \"Type\" WHEN 'Income' THEN 'Income' WHEN 'Expense' THEN 'Expense' END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Id_Kind",
                table: "Categories",
                columns: new[] { "Id", "Kind" },
                unique: true);

            // Not in the model (see TransactionConfiguration): it cannot hold a foreign key to a column that changes.
            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryId_CategoryKind",
                table: "Transactions",
                columns: new[] { "CategoryId", "CategoryKind" },
                principalTable: "Categories",
                principalColumns: new[] { "Id", "Kind" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Categories_CategoryId_CategoryKind",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Id_Kind",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "CategoryKind",
                table: "Transactions");
        }
    }
}
