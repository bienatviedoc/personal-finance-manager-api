using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BudgetWarningPercent",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CategoryKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true, computedColumnSql: "'Expense'", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.CheckConstraint("CK_Budgets_Amount_Not_Negative", "\"Amount\" >= 0");
                    table.ForeignKey(
                        name: "FK_Budgets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_BudgetWarningPercent_1_To_100",
                table: "Users",
                sql: "\"BudgetWarningPercent\" BETWEEN 1 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_CategoryId_Month",
                table: "Budgets",
                columns: new[] { "UserId", "CategoryId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_Month",
                table: "Budgets",
                columns: new[] { "UserId", "Month" });

            // The two foreign keys below are not in the model (see ModuleReferences): it cannot hold one to columns
            // that change. A budget's currency is its user's base currency, which locks it once a budget exists.
            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Users_UserId_Currency",
                table: "Budgets",
                columns: new[] { "UserId", "Currency" },
                principalTable: "Users",
                principalColumns: new[] { "Id", "BaseCurrency" },
                onDelete: ReferentialAction.Restrict);

            // A budget's category is an expense category.
            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Categories_CategoryId_CategoryKind",
                table: "Budgets",
                columns: new[] { "CategoryId", "CategoryKind" },
                principalTable: "Categories",
                principalColumns: new[] { "Id", "Kind" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_BudgetWarningPercent_1_To_100",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BudgetWarningPercent",
                table: "Users");
        }
    }
}
