using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLines", x => x.Id);
                    table.CheckConstraint("CK_TransactionLines_Amount_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_TransactionLines_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionLines_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_CategoryId_TransactionId",
                table: "TransactionLines",
                columns: new[] { "CategoryId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_TransactionId_Position",
                table: "TransactionLines",
                columns: new[] { "TransactionId", "Position" });

            // Not in the model (see TransactionConfiguration): it cannot hold a foreign key to a column that changes.
            migrationBuilder.AddForeignKey(
                name: "FK_TransactionLines_Categories_CategoryId_CategoryKind",
                table: "TransactionLines",
                columns: new[] { "CategoryId", "CategoryKind" },
                principalTable: "Categories",
                principalColumns: new[] { "Id", "Kind" },
                onDelete: ReferentialAction.Restrict);

            // Every transaction recorded before there were lines gets its one line: its category and whole amount.
            migrationBuilder.Sql(
                """
                INSERT INTO "TransactionLines" ("Id", "TransactionId", "Position", "CategoryId", "CategoryKind", "Amount", "Note")
                SELECT gen_random_uuid(), "Id", 0, "CategoryId", "CategoryKind", "Amount", NULL
                FROM "Transactions"
                WHERE "CategoryId" IS NOT NULL AND "CategoryKind" IS NOT NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionLines");
        }
    }
}
