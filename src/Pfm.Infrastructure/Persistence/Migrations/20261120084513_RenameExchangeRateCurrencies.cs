using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameExchangeRateCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "QuoteCurrency",
                table: "ExchangeRates",
                newName: "ToCurrency");

            migrationBuilder.RenameColumn(
                name: "BaseCurrency",
                table: "ExchangeRates",
                newName: "FromCurrency");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ToCurrency",
                table: "ExchangeRates",
                newName: "QuoteCurrency");

            migrationBuilder.RenameColumn(
                name: "FromCurrency",
                table: "ExchangeRates",
                newName: "BaseCurrency");
        }
    }
}
