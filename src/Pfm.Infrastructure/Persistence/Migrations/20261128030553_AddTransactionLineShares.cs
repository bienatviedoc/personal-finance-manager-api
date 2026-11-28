using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The view the database adds up spent and income from (`LineShare`): each line of a transaction with its share
    /// of the transaction's amount in the base currency, shared as `Transaction.SpentAmountIn` shares it. A line with
    /// the whole amount (one that is not split) takes the whole converted amount. A line of a split takes its part,
    /// rounded half away from zero to the base currency's decimals (`Currencies.DecimalPlaces`: none for VND, two
    /// for the others), and the last line, the one at the highest position, takes what the others left. The view is a
    /// plain join, so every condition on the transaction (user, dates, account, type) reaches the tables' indexes;
    /// only a split's lines look at its other lines.
    /// </summary>
    public partial class AddTransactionLineShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW "TransactionLineShares" AS
                SELECT t."UserId", t."Id" AS "TransactionId", t."AccountId", t."Date", t."Type", t."IsRefund", t."DeletedAt", t."Payee", l."CategoryId",
                    CASE
                        WHEN t."BaseCurrencyAmount" IS NULL THEN NULL
                        WHEN l."Amount" = t."Amount" THEN t."BaseCurrencyAmount"
                        WHEN EXISTS (
                            SELECT 1 FROM "TransactionLines" later
                            WHERE later."TransactionId" = l."TransactionId" AND later."Position" > l."Position")
                        THEN round(t."BaseCurrencyAmount" * l."Amount" / t."Amount", CASE t."BaseCurrency" WHEN 'VND' THEN 0 ELSE 2 END)
                        ELSE t."BaseCurrencyAmount" - (
                            SELECT coalesce(sum(round(t."BaseCurrencyAmount" * earlier."Amount" / t."Amount", CASE t."BaseCurrency" WHEN 'VND' THEN 0 ELSE 2 END)), 0)
                            FROM "TransactionLines" earlier
                            WHERE earlier."TransactionId" = l."TransactionId" AND earlier."Position" < l."Position")
                    END AS "BaseCurrencyAmount"
                FROM "Transactions" t
                JOIN "TransactionLines" l ON l."TransactionId" = t."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP VIEW "TransactionLineShares";""");
        }
    }
}
