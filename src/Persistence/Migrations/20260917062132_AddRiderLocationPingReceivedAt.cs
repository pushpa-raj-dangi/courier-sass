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

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAt",
                table: "RiderLocationPings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

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
