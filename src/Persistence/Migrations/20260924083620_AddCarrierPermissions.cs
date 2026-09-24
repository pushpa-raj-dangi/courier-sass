using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Key" },
                values: new object[,]
                {
                    { new Guid("6ded795a-0276-2fa3-5c04-3d79dedea070"), "Manage international carrier accounts and credentials", "international.carrier.manage" },
                    { new Guid("82fdcb4e-8ed8-7a3b-8c02-5955426b41a6"), "Record carrier tracking events and override international exception statuses", "international.tracking.manage" },
                    { new Guid("f93c2dd8-d038-695e-4cab-451b8a797d87"), "Book, cancel and link international carrier bookings", "international.book" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("6ded795a-0276-2fa3-5c04-3d79dedea070"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("82fdcb4e-8ed8-7a3b-8c02-5955426b41a6"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("f93c2dd8-d038-695e-4cab-451b8a797d87"));
        }
    }
}
