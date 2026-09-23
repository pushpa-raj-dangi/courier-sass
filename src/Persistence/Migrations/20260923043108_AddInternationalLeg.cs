using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternationalLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InternationalLegs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentsDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CountryOfOrigin = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Incoterm = table.Column<int>(type: "integer", nullable: false),
                    ExportLicenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomsCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProvisionalTotalChargeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProvisionalTotalChargeCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsProvisionalPricing = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternationalLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternationalLegs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomsItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternationalLegId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    HsCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomsValueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomsValueCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomsItems_InternationalLegs_InternationalLegId",
                        column: x => x.InternationalLegId,
                        principalTable: "InternationalLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomsItems_InternationalLegId",
                table: "CustomsItems",
                column: "InternationalLegId");

            migrationBuilder.CreateIndex(
                name: "IX_InternationalLegs_OrganizationId",
                table: "InternationalLegs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternationalLegs_ShipmentId",
                table: "InternationalLegs",
                column: "ShipmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomsItems");

            migrationBuilder.DropTable(
                name: "InternationalLegs");
        }
    }
}
