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
    /// only a split's lines look at its other lines. A share is null while the transaction is not converted.
    /// </summary>
    public partial class AddTransactionLineShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A split's lines are shared by a function, called only for them: declared cheap, so the planner costs the
            // view as the join it mostly is. Written out in the view, the subqueries are costed for every line, which
            // makes a plain sum look expensive enough to be compiled (JIT) first, which takes longer than the sum.
            migrationBuilder.Sql("""
                CREATE FUNCTION "SplitLineShare"(transaction_id uuid, line_position integer, whole numeric, amount numeric, line_amount numeric, decimals integer)
                RETURNS numeric
                LANGUAGE sql STABLE PARALLEL SAFE COST 10
                AS $$
                    SELECT CASE
                        WHEN EXISTS (
                            SELECT 1 FROM "TransactionLines" later
                            WHERE later."TransactionId" = transaction_id AND later."Position" > line_position)
                        THEN round(whole * line_amount / amount, decimals)
                        ELSE whole - (
                            SELECT coalesce(sum(round(whole * earlier."Amount" / amount, decimals)), 0)
                            FROM "TransactionLines" earlier
                            WHERE earlier."TransactionId" = transaction_id AND earlier."Position" < line_position)
                    END
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW "TransactionLineShares" AS
                SELECT t."UserId", t."Id" AS "TransactionId", t."AccountId", t."Date", t."Type", t."IsRefund", t."DeletedAt", t."Payee", l."CategoryId",
                    CASE
                        WHEN l."Amount" = t."Amount" THEN t."BaseCurrencyAmount"
                        ELSE "SplitLineShare"(t."Id", l."Position", t."BaseCurrencyAmount", t."Amount", l."Amount", CASE t."BaseCurrency" WHEN 'VND' THEN 0 ELSE 2 END)
                    END AS "BaseCurrencyAmount"
                FROM "Transactions" t
                JOIN "TransactionLines" l ON l."TransactionId" = t."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP VIEW "TransactionLineShares";""");
            migrationBuilder.Sql("""DROP FUNCTION "SplitLineShare";""");
        }
    }
}
