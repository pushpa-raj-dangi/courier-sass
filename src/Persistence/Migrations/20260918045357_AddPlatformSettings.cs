using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettingDefinitions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConfigPath = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettingDefinitions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    IsOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PlatformSettings_PlatformSettingDefinitions_Key",
                        column: x => x.Key,
                        principalTable: "PlatformSettingDefinitions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PlatformSettingDefinitions",
                columns: new[] { "Key", "Category", "ConfigPath", "Description", "IsSecret", "RequiresConfirmation", "ValueType" },
                values: new object[,]
                {
                    { "Billing.GraceDays", "Branding", "Billing:GraceDays", "Days after an invoice's due date before an organization is suspended.", false, false, "Int" },
                    { "Esewa.FormUrl", "PaymentGateway", "Esewa:FormUrl", "eSewa checkout form endpoint.", false, false, "String" },
                    { "Esewa.ProductCode", "PaymentGateway", "Esewa:ProductCode", "eSewa merchant product code.", false, false, "String" },
                    { "Esewa.SecretKey", "PaymentGateway", "Esewa:SecretKey", "eSewa HMAC signing secret.", true, false, "String" },
                    { "Esewa.StatusCheckUrl", "PaymentGateway", "Esewa:StatusCheckUrl", "eSewa server-to-server status check endpoint.", false, false, "String" },
                    { "Frontend.BaseUrl", "Branding", "Frontend:BaseUrl", "Base URL used to build links in outgoing emails/notifications.", false, false, "String" },
                    { "Jwt.AccessTokenMinutes", "Security", "Jwt:AccessTokenMinutes", "Access token lifetime, in minutes.", false, false, "Int" },
                    { "Jwt.Audience", "Security", "Jwt:Audience", "JWT audience claim.", false, false, "String" },
                    { "Jwt.Issuer", "Security", "Jwt:Issuer", "JWT issuer claim.", false, false, "String" },
                    { "Jwt.SigningKey", "Security", "Jwt:SigningKey", "JWT signing key. Rotating this logs out every user platform-wide.", true, true, "String" },
                    { "Khalti.InitiateUrl", "PaymentGateway", "Khalti:InitiateUrl", "Khalti payment initiate endpoint.", false, false, "String" },
                    { "Khalti.LookupUrl", "PaymentGateway", "Khalti:LookupUrl", "Khalti payment lookup endpoint.", false, false, "String" },
                    { "Khalti.SecretKey", "PaymentGateway", "Khalti:SecretKey", "Khalti live secret key.", true, false, "String" },
                    { "Khalti.WebsiteUrl", "PaymentGateway", "Khalti:WebsiteUrl", "Website URL Khalti associates with this integration.", false, false, "String" },
                    { "RateLimiting.ApiKeyRequestsPerMinute", "Security", "RateLimiting:ApiKeyRequestsPerMinute", "Per-API-key request limit per minute.", false, false, "Int" },
                    { "RateLimiting.PublicTrackingRequestsPerMinute", "Security", "RateLimiting:PublicTrackingRequestsPerMinute", "Per-IP public tracking request limit per minute.", false, false, "Int" },
                    { "Smtp.From", "Notifications", "Smtp:From", "From address for outgoing email.", false, false, "String" },
                    { "Smtp.Host", "Notifications", "Smtp:Host", "SMTP server host.", false, false, "String" },
                    { "Smtp.Port", "Notifications", "Smtp:Port", "SMTP server port.", false, false, "Int" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "PlatformSettingDefinitions");
        }
    }
}
