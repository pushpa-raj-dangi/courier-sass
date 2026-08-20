using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Awb = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentBranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentCustodianUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentCustodianRiderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStatus = table.Column<int>(type: "integer", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    LengthCm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    WidthCm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    DeclaredValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CodAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WantsInsurance = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InsuranceCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherCharges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecipientProvinceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientDistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientLocalLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientWardNumber = table.Column<int>(type: "integer", nullable: false),
                    RecipientAddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SenderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenderPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SenderProvinceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderDistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderLocalLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderWardNumber = table.Column<int>(type: "integer", nullable: false),
                    SenderAddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Branches_CurrentBranchId",
                        column: x => x.CurrentBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Branches_DestinationBranchId",
                        column: x => x.DestinationBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Branches_OriginBranchId",
                        column: x => x.OriginBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Districts_RecipientDistrictId",
                        column: x => x.RecipientDistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Districts_SenderDistrictId",
                        column: x => x.SenderDistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_LocalLevels_RecipientLocalLevelId",
                        column: x => x.RecipientLocalLevelId,
                        principalTable: "LocalLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_LocalLevels_SenderLocalLevelId",
                        column: x => x.SenderLocalLevelId,
                        principalTable: "LocalLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Provinces_RecipientProvinceId",
                        column: x => x.RecipientProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Provinces_SenderProvinceId",
                        column: x => x.SenderProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RiderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentEvents_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentItems_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_BranchId",
                table: "ShipmentEvents",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_ShipmentId_Timestamp",
                table: "ShipmentEvents",
                columns: new[] { "ShipmentId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentItems_ShipmentId",
                table: "ShipmentItems",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CurrentBranchId",
                table: "Shipments",
                column: "CurrentBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_DestinationBranchId",
                table: "Shipments",
                column: "DestinationBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_MerchantId",
                table: "Shipments",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrganizationId",
                table: "Shipments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrganizationId_Awb",
                table: "Shipments",
                columns: new[] { "OrganizationId", "Awb" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OriginBranchId",
                table: "Shipments",
                column: "OriginBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_RecipientDistrictId",
                table: "Shipments",
                column: "RecipientDistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_RecipientLocalLevelId",
                table: "Shipments",
                column: "RecipientLocalLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_RecipientProvinceId",
                table: "Shipments",
                column: "RecipientProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_SenderDistrictId",
                table: "Shipments",
                column: "SenderDistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_SenderLocalLevelId",
                table: "Shipments",
                column: "SenderLocalLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_SenderProvinceId",
                table: "Shipments",
                column: "SenderProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ServiceTypeId",
                table: "Shipments",
                column: "ServiceTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentEvents");

            migrationBuilder.DropTable(
                name: "ShipmentItems");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
