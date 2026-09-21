using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationLocaleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarPreference",
                table: "OrganizationSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Gregorian");

            migrationBuilder.AddColumn<string>(
                name: "DefaultLocale",
                table: "OrganizationSettings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarPreference",
                table: "OrganizationSettings");

            migrationBuilder.DropColumn(
                name: "DefaultLocale",
                table: "OrganizationSettings");
        }
    }
}
