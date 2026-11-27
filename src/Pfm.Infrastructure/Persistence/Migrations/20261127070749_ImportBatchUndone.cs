using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatchUndone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatches_Status",
                table: "ImportBatches");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UndoneAt",
                table: "ImportBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatches_Status",
                table: "ImportBatches",
                sql: "\"Status\" IN ('Uploaded', 'Committing', 'Committed', 'Failed', 'Undone')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatches_Status",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "UndoneAt",
                table: "ImportBatches");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatches_Status",
                table: "ImportBatches",
                sql: "\"Status\" IN ('Uploaded', 'Committing', 'Committed', 'Failed')");
        }
    }
}
