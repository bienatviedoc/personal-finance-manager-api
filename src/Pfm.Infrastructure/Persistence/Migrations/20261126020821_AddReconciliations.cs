using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReconciled",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reconciliations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReconciliationId",
                table: "Transactions",
                column: "ReconciliationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Reconciled_Is_Ticked",
                table: "Transactions",
                sql: "NOT \"IsReconciled\" OR \"ReconciliationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliations_AccountId_Id",
                table: "Reconciliations",
                columns: new[] { "AccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliations_AccountId_InProgress",
                table: "Reconciliations",
                column: "AccountId",
                unique: true,
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Reconciliations_ReconciliationId",
                table: "Transactions",
                column: "ReconciliationId",
                principalTable: "Reconciliations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Reconciliations_ReconciliationId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Reconciliations");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ReconciliationId",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Reconciled_Is_Ticked",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsReconciled",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReconciliationId",
                table: "Transactions");
        }
    }
}
