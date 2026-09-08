using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSuspensionAndImpersonationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "Organizations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SuspendedBySystem",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonatedByUserId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SuspendedBySystem",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ImpersonatedByUserId",
                table: "AuditLogs");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionPlan",
                table: "Organizations",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
