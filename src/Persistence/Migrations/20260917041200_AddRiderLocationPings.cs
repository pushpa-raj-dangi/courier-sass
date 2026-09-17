using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderLocationPings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiderLocationPings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    AccuracyMeters = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderLocationPings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderLocationPings_DeliveryRuns_DeliveryRunId",
                        column: x => x.DeliveryRunId,
                        principalTable: "DeliveryRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RiderLocationPings_Riders_RiderId",
                        column: x => x.RiderId,
                        principalTable: "Riders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationPings_DeliveryRunId",
                table: "RiderLocationPings",
                column: "DeliveryRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationPings_RecordedAt",
                table: "RiderLocationPings",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationPings_RiderId_RecordedAt",
                table: "RiderLocationPings",
                columns: new[] { "RiderId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderLocationPings");
        }
    }
}
