using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchDeliveryZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryZoneId",
                table: "Branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_DeliveryZoneId",
                table: "Branches",
                column: "DeliveryZoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_DeliveryZones_DeliveryZoneId",
                table: "Branches",
                column: "DeliveryZoneId",
                principalTable: "DeliveryZones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_DeliveryZones_DeliveryZoneId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_DeliveryZoneId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "DeliveryZoneId",
                table: "Branches");
        }
    }
}
