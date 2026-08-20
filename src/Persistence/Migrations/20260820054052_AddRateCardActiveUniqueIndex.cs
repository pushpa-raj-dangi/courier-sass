using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRateCardActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RateCards_OrganizationId",
                table: "RateCards");

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_OrganizationId_ActiveUnique",
                table: "RateCards",
                column: "OrganizationId",
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RateCards_OrganizationId_ActiveUnique",
                table: "RateCards");

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_OrganizationId",
                table: "RateCards",
                column: "OrganizationId");
        }
    }
}
