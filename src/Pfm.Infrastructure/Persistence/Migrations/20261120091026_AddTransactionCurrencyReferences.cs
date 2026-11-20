using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionCurrencyReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_Id_BaseCurrency",
                table: "Users",
                columns: new[] { "Id", "BaseCurrency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Id_Currency",
                table: "Accounts",
                columns: new[] { "Id", "Currency" },
                unique: true);

            // The two foreign keys are not in the model (see ModuleReferences): it cannot hold one to columns
            // that change, or one checked at commit.
            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId_Currency",
                table: "Transactions",
                columns: new[] { "AccountId", "Currency" },
                principalTable: "Accounts",
                principalColumns: new[] { "Id", "Currency" },
                onDelete: ReferentialAction.Restrict);

            // Checked at commit: a change of base currency saves the user and their transactions together.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Transactions" ADD CONSTRAINT "FK_Transactions_Users_UserId_BaseCurrency"
                    FOREIGN KEY ("UserId", "BaseCurrency") REFERENCES "Users" ("Id", "BaseCurrency")
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Users_UserId_BaseCurrency",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId_Currency",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Users_Id_BaseCurrency",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Id_Currency",
                table: "Accounts");
        }
    }
}
