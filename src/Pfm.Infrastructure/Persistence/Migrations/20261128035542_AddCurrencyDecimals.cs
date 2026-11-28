using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The database's one answer to how many decimals an amount in a currency has, the function `CurrencyDecimals`,
    /// which the view `TransactionLineShares` rounds a split's shares with. It answers what `Currencies.DecimalPlaces`
    /// does, and `MigrationTests` checks the two agree on every currency: it names each currency, and answers null
    /// for any other code, so a currency added to the domain fails that test until a migration adds it here too.
    /// </summary>
    public partial class AddCurrencyDecimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Immutable and plain SQL, so the planner inlines it where it is called: the view costs what it did.
            migrationBuilder.Sql("""
                CREATE FUNCTION "CurrencyDecimals"(code text)
                RETURNS integer
                LANGUAGE sql IMMUTABLE PARALLEL SAFE
                AS $$
                    SELECT CASE code WHEN 'VND' THEN 0 WHEN 'USD' THEN 2 WHEN 'EUR' THEN 2 END
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW "TransactionLineShares" AS
                SELECT t."UserId", t."Id" AS "TransactionId", t."AccountId", t."Date", t."Type", t."IsRefund", t."DeletedAt", t."Payee", t."Currency",
                    l."CategoryId", l."Amount",
                    CASE
                        WHEN l."Amount" = t."Amount" THEN t."BaseCurrencyAmount"
                        ELSE "SplitLineShare"(t."Id", l."Position", t."BaseCurrencyAmount", t."Amount", l."Amount", "CurrencyDecimals"(t."BaseCurrency"))
                    END AS "BaseCurrencyAmount"
                FROM "Transactions" t
                JOIN "TransactionLines" l ON l."TransactionId" = t."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW "TransactionLineShares" AS
                SELECT t."UserId", t."Id" AS "TransactionId", t."AccountId", t."Date", t."Type", t."IsRefund", t."DeletedAt", t."Payee", t."Currency",
                    l."CategoryId", l."Amount",
                    CASE
                        WHEN l."Amount" = t."Amount" THEN t."BaseCurrencyAmount"
                        ELSE "SplitLineShare"(t."Id", l."Position", t."BaseCurrencyAmount", t."Amount", l."Amount", CASE t."BaseCurrency" WHEN 'VND' THEN 0 ELSE 2 END)
                    END AS "BaseCurrencyAmount"
                FROM "Transactions" t
                JOIN "TransactionLines" l ON l."TransactionId" = t."Id";
                """);
            migrationBuilder.Sql("""DROP FUNCTION "CurrencyDecimals";""");
        }
    }
}
