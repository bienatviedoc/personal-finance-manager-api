using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurringRuleId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextRunDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SettledThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringRules", x => x.Id);
                    table.CheckConstraint("CK_RecurringRules_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_RecurringRules_Ends_After_It_Starts", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_RecurringRules_Interval_Positive", "\"Interval\" >= 1");
                    table.CheckConstraint("CK_RecurringRules_Type_IncomeOrExpense", "\"Type\" IN ('Income', 'Expense')");
                    table.ForeignKey(
                        name: "FK_RecurringRules_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RecurringRuleId",
                table: "Transactions",
                column: "RecurringRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_AccountId",
                table: "RecurringRules",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_CategoryId",
                table: "RecurringRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_NextRunDate_Due",
                table: "RecurringRules",
                column: "NextRunDate",
                filter: "\"NextRunDate\" IS NOT NULL AND NOT \"IsPaused\"");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_UserId",
                table: "RecurringRules",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_RecurringRules_RecurringRuleId",
                table: "Transactions",
                column: "RecurringRuleId",
                principalTable: "RecurringRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_RecurringRules_RecurringRuleId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "RecurringRules");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RecurringRuleId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringRuleId",
                table: "Transactions");
        }
    }
}
