using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSettings",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupportEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SupportPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AddressLine = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessHours = table.Column<string>(type: "text", nullable: true),
                    ProfileUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProfileUpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefreshTokenLifetimeDays = table.Column<int>(type: "integer", nullable: false),
                    MinPasswordLength = table.Column<int>(type: "integer", nullable: false),
                    SecurityUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecurityUpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultServiceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultOriginBranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultsUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DefaultsUpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSettings", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_OrganizationSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Key" },
                values: new object[,]
                {
                    { new Guid("68dee43b-1b9f-0860-374d-d9663127784f"), "Manage organization profile, security policy and operational defaults", "org.settings.manage" },
                    { new Guid("974a63bd-871c-1917-a8e9-9288f79bf8cf"), "Manage notification templates", "notification_template.manage" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationSettings");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("68dee43b-1b9f-0860-374d-d9663127784f"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("974a63bd-871c-1917-a8e9-9288f79bf8cf"));
        }
    }
}
