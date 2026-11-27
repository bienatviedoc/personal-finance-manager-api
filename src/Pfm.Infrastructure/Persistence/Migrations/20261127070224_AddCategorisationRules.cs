using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategorisationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategorisationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MatchType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorisationRules", x => x.Id);
                    table.CheckConstraint("CK_CategorisationRules_MatchType", "\"MatchType\" IN ('Contains', 'StartsWith', 'Exactly')");
                    table.CheckConstraint("CK_CategorisationRules_Priority_Not_Negative", "\"Priority\" >= 0");
                    table.ForeignKey(
                        name: "FK_CategorisationRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategorisationRules_CategoryId",
                table: "CategorisationRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategorisationRules_UserId",
                table: "CategorisationRules",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategorisationRules");
        }
    }
}
