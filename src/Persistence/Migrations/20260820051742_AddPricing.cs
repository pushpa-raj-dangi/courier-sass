using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateCards_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeightSlabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MinWeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    MaxWeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightSlabs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RateCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CalculationType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdditionalCharges_RateCards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "RateCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MerchantRateCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RateCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverrideDiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantRateCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantRateCards_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantRateCards_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantRateCards_RateCards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "RateCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZoneRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RateCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeightSlabId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoneRates_DeliveryZones_DestinationZoneId",
                        column: x => x.DestinationZoneId,
                        principalTable: "DeliveryZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneRates_DeliveryZones_OriginZoneId",
                        column: x => x.OriginZoneId,
                        principalTable: "DeliveryZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneRates_RateCards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "RateCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneRates_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZoneRates_WeightSlabs_WeightSlabId",
                        column: x => x.WeightSlabId,
                        principalTable: "WeightSlabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ServiceTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("35f65696-78ca-dd69-3b4f-49e2f8b7d117"), 4, "Special" },
                    { new Guid("770222a2-ccaa-4f0a-44a3-a5a4fb234b24"), 3, "Heavy" },
                    { new Guid("8dc124a1-a760-4e45-99b5-cf0151bc67f2"), 1, "Express" },
                    { new Guid("d38aa0e3-a1ad-fb1c-0d5b-98583748a878"), 0, "Standard" },
                    { new Guid("d916f737-bf5d-bd13-03cd-e936dbd5fd80"), 2, "Same Day" }
                });

            migrationBuilder.InsertData(
                table: "WeightSlabs",
                columns: new[] { "Id", "MaxWeightKg", "MinWeightKg" },
                values: new object[,]
                {
                    { new Guid("1ff8a68e-6f46-8023-939c-4ab33fd8d2ee"), 1m, 0.5m },
                    { new Guid("6d201205-54a0-2794-5765-daecaaa6c8b2"), null, 10m },
                    { new Guid("810737a0-46d8-02e3-f8f6-cf3d81326cb2"), 2m, 1m },
                    { new Guid("96fb9231-5071-24c2-1647-6196c10ccea0"), 5m, 2m },
                    { new Guid("b3fd6fc7-355b-9b72-38fa-c966ba2ec0e5"), 10m, 5m },
                    { new Guid("c9190fc8-1c64-36b9-6787-14c9470ac927"), 0.5m, 0m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalCharges_RateCardId",
                table: "AdditionalCharges",
                column: "RateCardId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRateCards_MerchantId",
                table: "MerchantRateCards",
                column: "MerchantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRateCards_OrganizationId",
                table: "MerchantRateCards",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRateCards_RateCardId",
                table: "MerchantRateCards",
                column: "RateCardId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_OrganizationId",
                table: "RateCards",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_OrganizationId_IsActive",
                table: "RateCards",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_Code",
                table: "ServiceTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRates_DestinationZoneId",
                table: "ZoneRates",
                column: "DestinationZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRates_OriginZoneId",
                table: "ZoneRates",
                column: "OriginZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRates_RateCardId",
                table: "ZoneRates",
                column: "RateCardId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRates_RateCardId_OriginZoneId_DestinationZoneId_Service~",
                table: "ZoneRates",
                columns: new[] { "RateCardId", "OriginZoneId", "DestinationZoneId", "ServiceTypeId", "WeightSlabId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRates_ServiceTypeId",
                table: "ZoneRates",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoneRates_WeightSlabId",
                table: "ZoneRates",
                column: "WeightSlabId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdditionalCharges");

            migrationBuilder.DropTable(
                name: "MerchantRateCards");

            migrationBuilder.DropTable(
                name: "ZoneRates");

            migrationBuilder.DropTable(
                name: "RateCards");

            migrationBuilder.DropTable(
                name: "ServiceTypes");

            migrationBuilder.DropTable(
                name: "WeightSlabs");
        }
    }
}
