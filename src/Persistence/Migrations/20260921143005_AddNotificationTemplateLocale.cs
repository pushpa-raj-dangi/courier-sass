using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplateLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationTemplates_OrganizationId_EventType_Channel",
                table: "NotificationTemplates");

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "NotificationTemplates",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_OrganizationId_EventType_Channel_Loca~",
                table: "NotificationTemplates",
                columns: new[] { "OrganizationId", "EventType", "Channel", "Locale" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationTemplates_OrganizationId_EventType_Channel_Loca~",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "NotificationTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_OrganizationId_EventType_Channel",
                table: "NotificationTemplates",
                columns: new[] { "OrganizationId", "EventType", "Channel" });
        }
    }
}
