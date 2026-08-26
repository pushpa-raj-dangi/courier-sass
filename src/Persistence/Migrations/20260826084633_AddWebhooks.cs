using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false),
                    Events = table.Column<string[]>(type: "text[]", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookSubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: true),
                    ResponseSnippet = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_WebhookSubscriptions_WebhookSubscriptionId",
                        column: x => x.WebhookSubscriptionId,
                        principalTable: "WebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_NextRetryAt",
                table: "WebhookDeliveries",
                column: "NextRetryAt",
                filter: "\"NextRetryAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookSubscriptionId_SourceEventId",
                table: "WebhookDeliveries",
                columns: new[] { "WebhookSubscriptionId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_MerchantId",
                table: "WebhookSubscriptions",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_OrganizationId",
                table: "WebhookSubscriptions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_OrganizationId_IsActive",
                table: "WebhookSubscriptions",
                columns: new[] { "OrganizationId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");
        }
    }
}
