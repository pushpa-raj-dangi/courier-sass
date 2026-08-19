using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixDeliveryZoneAreaRestrictAndBranchColumnDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryZoneAreas_Areas_CoveredAreasId",
                table: "DeliveryZoneAreas");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryZoneAreas_Areas_CoveredAreasId",
                table: "DeliveryZoneAreas",
                column: "CoveredAreasId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The AddBranchGeographyFields migration used AddColumn(defaultValue:) to backfill
            // pre-existing dev-fixture rows to a valid Kathmandu geography chain. Postgres keeps
            // that server-side default permanently even though the EF model itself was never
            // configured with a default, so drop it here to bring DB and model back in sync.
            // Any insert must now explicitly supply these values or fail loudly.
            migrationBuilder.Sql(@"ALTER TABLE ""Branches""
    ALTER COLUMN ""ProvinceId"" DROP DEFAULT,
    ALTER COLUMN ""DistrictId"" DROP DEFAULT,
    ALTER COLUMN ""LocalLevelId"" DROP DEFAULT,
    ALTER COLUMN ""WardNumber"" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down() does not restore the original backfill defaults from
            // AddBranchGeographyFields. Those defaults were a one-time backfill convenience and
            // are not part of the intended model; restoring them here would reintroduce the
            // drift this migration exists to remove.
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryZoneAreas_Areas_CoveredAreasId",
                table: "DeliveryZoneAreas");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryZoneAreas_Areas_CoveredAreasId",
                table: "DeliveryZoneAreas",
                column: "CoveredAreasId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
