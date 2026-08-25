using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AfterValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Adjustments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BranchCashSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReconciledByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchCashSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchCashSettlements_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BranchCashSettlements_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReferenceEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MerchantSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TotalPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantSettlements_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiderCashSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RiderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeclaredAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShipmentCount = table.Column<int>(type: "integer", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchCashSettlementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderCashSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiderCashSettlements_BranchCashSettlements_BranchCashSettle~",
                        column: x => x.BranchCashSettlementId,
                        principalTable: "BranchCashSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderCashSettlements_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiderCashSettlements_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MerchantSettlementItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantSettlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CODAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CourierFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantSettlementItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantSettlementItems_MerchantSettlements_MerchantSettlem~",
                        column: x => x.MerchantSettlementId,
                        principalTable: "MerchantSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CODCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShipmentTotalCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CollectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CollectedByRiderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryProofId = table.Column<Guid>(type: "uuid", nullable: true),
                    RiderCashSettlementId = table.Column<Guid>(type: "uuid", nullable: true),
                    MerchantSettlementItemId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CODCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CODCollections_MerchantSettlementItems_MerchantSettlementIt~",
                        column: x => x.MerchantSettlementItemId,
                        principalTable: "MerchantSettlementItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CODCollections_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CODCollections_RiderCashSettlements_RiderCashSettlementId",
                        column: x => x.RiderCashSettlementId,
                        principalTable: "RiderCashSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_EntityType_EntityId",
                table: "Adjustments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustments_OrganizationId",
                table: "Adjustments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchCashSettlements_BranchId",
                table: "BranchCashSettlements",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchCashSettlements_OrganizationId",
                table: "BranchCashSettlements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_BranchId",
                table: "CODCollections",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_MerchantId",
                table: "CODCollections",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_MerchantSettlementItemId",
                table: "CODCollections",
                column: "MerchantSettlementItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_OrganizationId",
                table: "CODCollections",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_RiderCashSettlementId",
                table: "CODCollections",
                column: "RiderCashSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_ShipmentId",
                table: "CODCollections",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CODCollections_Status",
                table: "CODCollections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_OrganizationId",
                table: "FinancialTransactions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_ReferenceEntityType_ReferenceEntityId",
                table: "FinancialTransactions",
                columns: new[] { "ReferenceEntityType", "ReferenceEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantSettlementItems_MerchantSettlementId",
                table: "MerchantSettlementItems",
                column: "MerchantSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantSettlementItems_ShipmentId",
                table: "MerchantSettlementItems",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantSettlements_MerchantId",
                table: "MerchantSettlements",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantSettlements_OrganizationId",
                table: "MerchantSettlements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderCashSettlements_BranchCashSettlementId",
                table: "RiderCashSettlements",
                column: "BranchCashSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderCashSettlements_BranchId",
                table: "RiderCashSettlements",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderCashSettlements_OrganizationId",
                table: "RiderCashSettlements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderCashSettlements_RiderId",
                table: "RiderCashSettlements",
                column: "RiderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Adjustments");

            migrationBuilder.DropTable(
                name: "CODCollections");

            migrationBuilder.DropTable(
                name: "FinancialTransactions");

            migrationBuilder.DropTable(
                name: "MerchantSettlementItems");

            migrationBuilder.DropTable(
                name: "RiderCashSettlements");

            migrationBuilder.DropTable(
                name: "MerchantSettlements");

            migrationBuilder.DropTable(
                name: "BranchCashSettlements");
        }
    }
}
