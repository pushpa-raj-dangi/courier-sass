using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNdr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ndrs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    AssignedStaffUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CustomerResponse = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FollowUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Resolution = table.Column<int>(type: "integer", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ndrs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ndrs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnShipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnAwb = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RtoReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnShipments_Branches_DestinationBranchId",
                        column: x => x.DestinationBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnShipments_Branches_OriginBranchId",
                        column: x => x.OriginBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnShipments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ndrs_OrganizationId",
                table: "Ndrs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Ndrs_ShipmentId",
                table: "Ndrs",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipments_DestinationBranchId",
                table: "ReturnShipments",
                column: "DestinationBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipments_OrganizationId",
                table: "ReturnShipments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipments_OrganizationId_ReturnAwb",
                table: "ReturnShipments",
                columns: new[] { "OrganizationId", "ReturnAwb" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipments_OriginalShipmentId",
                table: "ReturnShipments",
                column: "OriginalShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnShipments_OriginBranchId",
                table: "ReturnShipments",
                column: "OriginBranchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ndrs");

            migrationBuilder.DropTable(
                name: "ReturnShipments");
        }
    }
}
