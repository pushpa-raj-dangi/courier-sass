using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchGeographyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default values below point at the real, already-seeded "Bagmati / Kathmandu /
            // Kathmandu Metropolitan City" chain (GeographyIds.ProvinceId("Bagmati") etc.), not
            // Guid.Empty. This backfills any Branch rows that predate this migration to a valid
            // geography chain so the FK constraints added below can be satisfied without deleting
            // data; every future insert goes through BranchService, which always supplies real
            // values and validates the full chain server-side, so this default is only ever hit
            // as a one-time backfill for pre-existing rows.
            migrationBuilder.AddColumn<Guid>(
                name: "DistrictId",
                table: "Branches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("4d4c70f0-1487-88fd-46f6-fa54f3aebfb2"));

            migrationBuilder.AddColumn<Guid>(
                name: "LocalLevelId",
                table: "Branches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("9af1740e-3ba8-827f-91fb-342823b2f14e"));

            migrationBuilder.AddColumn<Guid>(
                name: "ProvinceId",
                table: "Branches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a"));

            migrationBuilder.AddColumn<int>(
                name: "WardNumber",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_DistrictId",
                table: "Branches",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_LocalLevelId",
                table: "Branches",
                column: "LocalLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_ProvinceId",
                table: "Branches",
                column: "ProvinceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_Districts_DistrictId",
                table: "Branches",
                column: "DistrictId",
                principalTable: "Districts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_LocalLevels_LocalLevelId",
                table: "Branches",
                column: "LocalLevelId",
                principalTable: "LocalLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_Provinces_ProvinceId",
                table: "Branches",
                column: "ProvinceId",
                principalTable: "Provinces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_Districts_DistrictId",
                table: "Branches");

            migrationBuilder.DropForeignKey(
                name: "FK_Branches_LocalLevels_LocalLevelId",
                table: "Branches");

            migrationBuilder.DropForeignKey(
                name: "FK_Branches_Provinces_ProvinceId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_DistrictId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_LocalLevelId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_ProvinceId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "DistrictId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "LocalLevelId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ProvinceId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "WardNumber",
                table: "Branches");
        }
    }
}
