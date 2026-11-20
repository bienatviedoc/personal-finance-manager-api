using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionBaseCurrencyAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseCurrency",
                table: "Transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "BaseCurrencyAmount",
                table: "Transactions",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Transactions",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "Transactions",
                type: "date",
                nullable: true);

            // Transactions recorded before this take their user's base currency. One already in it needs no rate;
            // the others are not converted until the next fetch of exchange rates converts them.
            migrationBuilder.Sql(
                """
                UPDATE "Transactions" AS t SET "BaseCurrency" = u."BaseCurrency" FROM "Users" AS u WHERE u."Id" = t."UserId";
                UPDATE "Transactions" SET "BaseCurrencyAmount" = "Amount", "ExchangeRate" = 1, "ExchangeRateDate" = "Date" WHERE "Currency" = "BaseCurrency";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_NotConverted",
                table: "Transactions",
                column: "UserId",
                filter: "\"BaseCurrencyAmount\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Converted_Or_Not",
                table: "Transactions",
                sql: "(\"BaseCurrencyAmount\" IS NULL) = (\"ExchangeRate\" IS NULL) AND (\"BaseCurrencyAmount\" IS NULL) = (\"ExchangeRateDate\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_NotConverted",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Converted_Or_Not",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BaseCurrency",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BaseCurrencyAmount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "Transactions");
        }
    }
}
