using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternationalPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Key" },
                values: new object[,]
                {
                    { new Guid("af904214-6ec5-b290-eb93-533850f77399"), "Receive international shipments from a carrier", "international.receive" },
                    { new Guid("bb12e4e4-0141-ed75-a510-2a959cd66bdc"), "Read international shipments", "international.read" },
                    { new Guid("bc08b9b0-966f-2ca3-7868-eb7d7c693b07"), "Manage international customs declarations", "international.manage_customs" },
                    { new Guid("dfd4fe0f-b9ee-e015-7c70-6329533eb2c9"), "Create international shipments", "international.create" },
                    { new Guid("f51d0581-9f51-df22-bb52-1537b4839773"), "Hand off international shipments to a carrier", "international.handoff" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("af904214-6ec5-b290-eb93-533850f77399"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bb12e4e4-0141-ed75-a510-2a959cd66bdc"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bc08b9b0-966f-2ca3-7868-eb7d7c693b07"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("dfd4fe0f-b9ee-e015-7c70-6329533eb2c9"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("f51d0581-9f51-df22-bb52-1537b4839773"));
        }
    }
}
