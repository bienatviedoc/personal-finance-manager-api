using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntriesGoWithTheirUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_AuditEntries_Users_UserId",
                table: "AuditEntries",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Append only (spec section 10): the database refuses to change an entry, and to delete one while its user's
            // row is there. Erasing the user deletes that row; the foreign key above then deletes their entries, which
            // nothing else can, not even while the user is deactivated and waiting out their 30 days.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION "AuditEntriesAppendOnly"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id" = OLD."UserId") THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'Audit entries are append-only: they cannot be %d.', lower(TG_OP)
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION "AuditEntriesAppendOnly"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND NOT EXISTS (
                        SELECT 1 FROM "Users" WHERE "Id" = OLD."UserId" AND "DeactivatedAt" IS NULL) THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'Audit entries are append-only: they cannot be %d.', lower(TG_OP)
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AuditEntries_Users_UserId",
                table: "AuditEntries");
        }
    }
}
