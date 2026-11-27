using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneImportCommittingPerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_AccountId",
                table: "ImportBatches");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_AccountId_Committing",
                table: "ImportBatches",
                column: "AccountId",
                unique: true,
                filter: "\"Status\" = 'Committing'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_AccountId_Committing",
                table: "ImportBatches");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_AccountId",
                table: "ImportBatches",
                column: "AccountId");
        }
    }
}
