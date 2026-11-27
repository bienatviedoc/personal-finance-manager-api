using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportFailureCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A reason written as English text before failures had codes: the one the worker could not say better.
            migrationBuilder.Sql("""
                UPDATE "ImportBatches" SET "Failure" = CASE
                    WHEN "Failure" LIKE 'The account has been archived%' THEN 'AccountArchived'
                    WHEN "Failure" LIKE '%currency has changed%' THEN 'AccountCurrencyChanged'
                    WHEN "Failure" LIKE '%categor%' THEN 'CategoryArchived'
                    ELSE 'TransactionRefused' END
                WHERE "Failure" IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Failure",
                table: "ImportBatches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Failure",
                table: "ImportBatches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}
