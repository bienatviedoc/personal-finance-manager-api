using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatchCommittedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CommittedOn",
                table: "ImportBatches",
                type: "date",
                nullable: true);

            // A batch committed before this column read its rows as of the worker's today; the day it was last changed is the nearest there is.
            migrationBuilder.Sql("UPDATE \"ImportBatches\" SET \"CommittedOn\" = (\"UpdatedAt\" AT TIME ZONE 'UTC')::date WHERE \"Status\" <> 'Uploaded';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommittedOn",
                table: "ImportBatches");
        }
    }
}
