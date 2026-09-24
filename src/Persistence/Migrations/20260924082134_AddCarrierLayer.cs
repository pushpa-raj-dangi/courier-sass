using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EncryptedCredentials = table.Column<string>(type: "text", nullable: true),
                    EncryptedWebhookSecret = table.Column<string>(type: "text", nullable: true),
                    WebhookPublicId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PollingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InternationalLegId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    CarrierAwb = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceLevel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LabelStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RawResponse = table.Column<string>(type: "jsonb", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BookedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CustodyStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierBookings_CarrierAccounts_CarrierAccountId",
                        column: x => x.CarrierAccountId,
                        principalTable: "CarrierAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierBookings_InternationalLegs_InternationalLegId",
                        column: x => x.InternationalLegId,
                        principalTable: "InternationalLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierBookings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierTrackingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierAwb = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CarrierEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CarrierStatusCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    EnteredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessingNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MappedShipmentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProcessingAttempts = table.Column<int>(type: "integer", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierTrackingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierTrackingEvents_CarrierAccounts_CarrierAccountId",
                        column: x => x.CarrierAccountId,
                        principalTable: "CarrierAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierTrackingEvents_CarrierBookings_CarrierBookingId",
                        column: x => x.CarrierBookingId,
                        principalTable: "CarrierBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierTrackingEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAccounts_OrganizationId",
                table: "CarrierAccounts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierAccounts_WebhookPublicId",
                table: "CarrierAccounts",
                column: "WebhookPublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookings_CarrierAccountId_CarrierAwb",
                table: "CarrierBookings",
                columns: new[] { "CarrierAccountId", "CarrierAwb" },
                unique: true,
                filter: "\"CarrierAwb\" IS NOT NULL AND \"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookings_InternationalLegId",
                table: "CarrierBookings",
                column: "InternationalLegId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierBookings_OrganizationId_IdempotencyKey",
                table: "CarrierBookings",
                columns: new[] { "OrganizationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTrackingEvents_CarrierAccountId_CarrierAwb",
                table: "CarrierTrackingEvents",
                columns: new[] { "CarrierAccountId", "CarrierAwb" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTrackingEvents_CarrierAccountId_CarrierEventId",
                table: "CarrierTrackingEvents",
                columns: new[] { "CarrierAccountId", "CarrierEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTrackingEvents_CarrierBookingId",
                table: "CarrierTrackingEvents",
                column: "CarrierBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTrackingEvents_OrganizationId",
                table: "CarrierTrackingEvents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierTrackingEvents_ProcessingStatus_OccurredAt",
                table: "CarrierTrackingEvents",
                columns: new[] { "ProcessingStatus", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierTrackingEvents");

            migrationBuilder.DropTable(
                name: "CarrierBookings");

            migrationBuilder.DropTable(
                name: "CarrierAccounts");
        }
    }
}
