using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringRuleMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecurringRules_NextRunDate_Due",
                table: "RecurringRules");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "RecurringRules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AutoPost");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_NextRunDate_Due",
                table: "RecurringRules",
                column: "NextRunDate",
                filter: "\"NextRunDate\" IS NOT NULL AND NOT \"IsPaused\" AND \"Mode\" = 'AutoPost'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringRules_Mode_AutoPostOrRemind",
                table: "RecurringRules",
                sql: "\"Mode\" IN ('AutoPost', 'Remind')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecurringRules_NextRunDate_Due",
                table: "RecurringRules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringRules_Mode_AutoPostOrRemind",
                table: "RecurringRules");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "RecurringRules");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_NextRunDate_Due",
                table: "RecurringRules",
                column: "NextRunDate",
                filter: "\"NextRunDate\" IS NOT NULL AND NOT \"IsPaused\"");
        }
    }
}
