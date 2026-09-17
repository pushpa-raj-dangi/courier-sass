using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderLocationPingReceivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RiderLocationPings_RecordedAt",
                table: "RiderLocationPings");

            // Nullable first, backfilled from the existing RecordedAt column, then locked to NOT
            // NULL — a static sentinel default (e.g. year 1) would make the retention job's
            // "ReceivedAt < cutoff" predicate treat every pre-existing row as already expired,
            // deleting real breadcrumb history on the very first run after this migration applies.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "RiderLocationPings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"RiderLocationPings\" SET \"ReceivedAt\" = \"RecordedAt\" WHERE \"ReceivedAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "RiderLocationPings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationPings_ReceivedAt",
                table: "RiderLocationPings",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RiderLocationPings_ReceivedAt",
                table: "RiderLocationPings");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "RiderLocationPings");

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationPings_RecordedAt",
                table: "RiderLocationPings",
                column: "RecordedAt");
        }
    }
}
