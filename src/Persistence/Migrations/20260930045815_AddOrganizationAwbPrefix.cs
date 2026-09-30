using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAwbPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backs the AwbPrefix column default (OrganizationConfiguration) — must exist before the column.
            migrationBuilder.Sql("CREATE SEQUENCE \"OrganizationAwbPrefixFallback\";");

            migrationBuilder.AddColumn<string>(
                name: "AwbPrefix",
                table: "Organizations",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValueSql: "'Q' || upper(lpad(to_hex(nextval('\"OrganizationAwbPrefixFallback\"')), 5, '0'))");

            // Backfill with the same rules as Identity.Domain.AwbPrefix: the base is the Slug
            // uppercased, non-alphanumerics stripped, first 6 chars (or the first 6 hex chars of the
            // Id for a symbol-only slug). The oldest organization per base keeps the base — so an
            // existing tenant's AWB format is unchanged unless it collided with an older tenant — and
            // each later one gets left(base, 6 - len(n)) || n with n = its rank in that base group.
            // A candidate still clashing with another row's prefix (groups sharing a stem, e.g.
            // INTLOR/INTLOX both reaching IN1000) re-rolls to first letter + 5 hex chars of a salted
            // hash of its Id until nothing clashes; a base always wins. Set-based, a few passes.
            // Already-issued AWBs are never rewritten (they are printed on labels).
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    passes int := 0;
                BEGIN
                    CREATE TEMP TABLE awb_prefix_backfill ON COMMIT DROP AS
                    SELECT "Id", "CreatedAt", base,
                           row_number() OVER (PARTITION BY base ORDER BY "CreatedAt", "Id") AS rn
                    FROM (SELECT "Id", "CreatedAt",
                                 COALESCE(NULLIF(left(regexp_replace(upper("Slug"), '[^A-Z0-9]', '', 'g'), 6), ''),
                                          upper(left(replace("Id"::text, '-', ''), 6))) AS base
                          FROM "Organizations") o;

                    ALTER TABLE awb_prefix_backfill ADD COLUMN prefix text;
                    UPDATE awb_prefix_backfill SET
                        prefix = CASE WHEN rn = 1 THEN base
                                      WHEN rn < 100000 THEN left(base, 6 - length(rn::text)) || rn::text
                                      ELSE left(base, 1) || upper(substr(md5("Id"::text), 1, 5)) END;

                    LOOP
                        UPDATE awb_prefix_backfill t
                        SET prefix = left(t.base, 1) || upper(substr(md5(t."Id"::text || passes::text), 1, 5))
                        FROM (SELECT "Id", row_number() OVER (PARTITION BY prefix ORDER BY (rn = 1) DESC, "CreatedAt", "Id") AS rk
                              FROM awb_prefix_backfill) ranked
                        WHERE ranked."Id" = t."Id" AND ranked.rk > 1;
                        EXIT WHEN NOT FOUND;
                        passes := passes + 1;
                        IF passes > 50 THEN
                            RAISE EXCEPTION 'AwbPrefix backfill did not converge';
                        END IF;
                    END LOOP;

                    UPDATE "Organizations" o SET "AwbPrefix" = t.prefix
                    FROM awb_prefix_backfill t WHERE t."Id" = o."Id";
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_AwbPrefix",
                table: "Organizations",
                column: "AwbPrefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_AwbPrefix",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AwbPrefix",
                table: "Organizations");

            migrationBuilder.Sql("DROP SEQUENCE \"OrganizationAwbPrefixFallback\";");
        }
    }
}
