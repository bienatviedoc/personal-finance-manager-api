using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "TransferId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransferId_Type",
                table: "Transactions",
                columns: new[] { "TransferId", "Type" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Transfer_Leg_Has_No_Category",
                table: "Transactions",
                sql: "\"TransferId\" IS NULL OR \"CategoryId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Transfer_Leg_Has_No_RecurringRule",
                table: "Transactions",
                sql: "\"TransferId\" IS NULL OR \"RecurringRuleId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Transfer_Leg_Has_TransferId",
                table: "Transactions",
                sql: "(\"Type\" IN ('TransferOut', 'TransferIn')) = (\"TransferId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_TransferId_Type",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Transfer_Leg_Has_No_Category",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Transfer_Leg_Has_No_RecurringRule",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Transfer_Leg_Has_TransferId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "Transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
