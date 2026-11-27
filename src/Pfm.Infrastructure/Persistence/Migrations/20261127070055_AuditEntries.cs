using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pfm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_UserId_Id",
                table: "AuditEntries",
                columns: new[] { "UserId", "Id" });

            // Append only (spec section 10): the database refuses to change an entry, and to delete one unless its user is
            // deactivated, which only erasing them after their 30 days does, or is gone already.
            migrationBuilder.Sql("""
                CREATE FUNCTION "AuditEntriesAppendOnly"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND NOT EXISTS (
                        SELECT 1 FROM "Users" WHERE "Id" = OLD."UserId" AND "DeactivatedAt" IS NULL) THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'Audit entries are append-only: they cannot be %d.', lower(TG_OP)
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER "TR_AuditEntries_AppendOnly" BEFORE UPDATE OR DELETE ON "AuditEntries"
                    FOR EACH ROW EXECUTE FUNCTION "AuditEntriesAppendOnly"();

                CREATE TRIGGER "TR_AuditEntries_NoTruncate" BEFORE TRUNCATE ON "AuditEntries"
                    FOR EACH STATEMENT EXECUTE FUNCTION "AuditEntriesAppendOnly"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.Sql("""DROP FUNCTION "AuditEntriesAppendOnly"();""");
        }
    }
}
