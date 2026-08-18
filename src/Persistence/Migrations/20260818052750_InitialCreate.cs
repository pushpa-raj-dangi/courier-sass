using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Before = table.Column<string>(type: "text", nullable: true),
                    After = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsCrossTenantAction = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubscriptionPlan = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsSystemDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchScope = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    PermissionsId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.PermissionsId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionsId",
                        column: x => x.PermissionsId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Key" },
                values: new object[,]
                {
                    { new Guid("10c82d42-61bf-c492-2ba6-b9e81cbe827e"), "Read merchants", "merchant.read" },
                    { new Guid("1156af97-c51a-937c-18d7-9c9efd89e2fd"), "Read riders", "rider.read" },
                    { new Guid("129fd727-7731-5255-f54b-922b439730a3"), "Delete an organization", "org.delete" },
                    { new Guid("180be3e2-e534-502c-bc61-7a28b0620e0d"), "Resolve NDRs", "ndr.resolve" },
                    { new Guid("21afb7ea-ac8f-43db-9d15-08cf84f2c19e"), "Read audit logs", "audit.read" },
                    { new Guid("2dba0086-9af3-5ef3-1087-de97cf6707e7"), "Create riders", "rider.create" },
                    { new Guid("2e3d6e8c-1168-351d-e1f9-2c25058227c2"), "Create branches", "branch.create" },
                    { new Guid("2f1d3988-bb08-0869-311a-0b04efd45888"), "Read pricing", "pricing.read" },
                    { new Guid("3487bc60-05d8-17b5-6d40-424d658d2ab9"), "Scan shipments", "shipment.scan" },
                    { new Guid("3612460b-1d5e-31b5-b108-b047bc1e9f22"), "Receive manifests", "manifest.receive" },
                    { new Guid("3e33972c-e329-47d1-6ebd-b38040f97ab3"), "Transfer organization ownership", "org.ownership.transfer" },
                    { new Guid("3f713b32-bf40-21c8-10dc-87bd2a748a65"), "Adjust COD figures", "cod.adjust" },
                    { new Guid("3fb38990-31a5-a3ac-5b60-f2c30712a039"), "Dispatch manifests", "manifest.dispatch" },
                    { new Guid("4168e8ce-ad31-3eda-4082-6b75fb0a84f6"), "Read reports", "report.read" },
                    { new Guid("49972bf2-5ad3-d0d6-dd47-fb43c0caf5d9"), "Manage pricing", "pricing.manage" },
                    { new Guid("55c6076f-a59e-5616-24b7-0e76d601446f"), "Update merchants", "merchant.update" },
                    { new Guid("5dbfbfc3-ad2b-fd0d-e06c-33732c6b00f0"), "Review merchant KYC", "merchant.kyc.review" },
                    { new Guid("5e90c562-20e0-8a44-8b25-3204265323d2"), "Manage organization billing", "org.billing.manage" },
                    { new Guid("63ac4a85-974a-3cc7-8f31-4aa8768192cb"), "Sort shipments", "shipment.sort" },
                    { new Guid("63f75b1c-eaa3-8b58-afce-91956fc58923"), "Complete deliveries", "delivery.complete" },
                    { new Guid("716c879a-c3ff-5e43-9e27-a2693070d004"), "Export reports", "report.export" },
                    { new Guid("7692c371-5963-d253-9a1c-242eccfb38d0"), "Reconcile COD", "cod.reconcile" },
                    { new Guid("7bdf80ef-d0b1-a6b1-20f3-c629d4c3f98d"), "Read branches", "branch.read" },
                    { new Guid("7dcc5da2-e2fd-669d-3bba-5e71630f8457"), "Manage roles", "role.manage" },
                    { new Guid("82e80bc9-03a9-7e83-7270-7e7bf9f1a197"), "Settle COD", "cod.settle" },
                    { new Guid("8348e135-3461-4351-1891-ed6fd80b30b3"), "Assign riders", "rider.assign" },
                    { new Guid("887b1fc6-6b59-3dff-7872-83a929c814ea"), "Manage RTOs", "rto.manage" },
                    { new Guid("88ca8356-cb8a-d90f-b8ea-7c697e214a3a"), "Read users", "user.read" },
                    { new Guid("88ff141e-a729-e493-b1fd-29d8f10ebe14"), "Update shipments", "shipment.update" },
                    { new Guid("8aacfc2e-9a7e-8481-2f05-67b72284ad7f"), "Create merchants", "merchant.create" },
                    { new Guid("8cfe526c-01ef-b686-1b71-9b3eb570f029"), "Create shipments", "shipment.create" },
                    { new Guid("9a326c03-5ba6-0f3a-122e-74d87fdd64f0"), "Create manifests", "manifest.create" },
                    { new Guid("9a5298a8-3429-f8c7-d00a-7dd9e9203232"), "Transfer shipments between branches", "shipment.transfer" },
                    { new Guid("a168cc3e-d9d1-4e95-93fc-036a5108cc00"), "Close manifests", "manifest.close" },
                    { new Guid("b9703328-2213-2aa8-4571-7c941e6e0b8c"), "Read shipments", "shipment.read" },
                    { new Guid("bc291b0b-ee15-07ad-a49e-05b26da793fd"), "Create users", "user.create" },
                    { new Guid("bc64ee04-cd04-339e-70c3-8266ede840b3"), "Seal manifests", "manifest.seal" },
                    { new Guid("bf904f45-0650-a603-ffa7-f5e3aa39b08a"), "Assign shipments to riders", "shipment.assign" },
                    { new Guid("c1090ed5-8754-f225-7720-af9065acf2b2"), "Read NDRs", "ndr.read" },
                    { new Guid("c2b432f8-df45-8144-8149-f309ea3cfe3e"), "Collect COD", "cod.collect" },
                    { new Guid("c87361e8-6608-b2ea-0389-5af279a5934d"), "Cancel shipments", "shipment.cancel" },
                    { new Guid("d4b745a2-4327-bdc2-3215-aa1d69d51020"), "Manage NDRs", "ndr.manage" },
                    { new Guid("d7d91d15-094a-9941-227b-426d493c0931"), "Assign delivery tasks", "delivery.assign" },
                    { new Guid("db8ce444-0483-013f-8028-6d39371c90c9"), "Update branches", "branch.update" },
                    { new Guid("e4d24ff1-f6ef-758f-9f06-4ee8c96c143d"), "Read COD records", "cod.read" },
                    { new Guid("ec7b91f1-e900-5a78-22c2-95d628ea1d01"), "Read RTOs", "rto.read" },
                    { new Guid("f193174e-6060-f3f8-e502-c456c5de7609"), "Update users", "user.update" },
                    { new Guid("f3d16cbd-30a7-3597-f5a7-ee2eeb6b3670"), "Record delivery attempts", "delivery.attempt" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "OrganizationId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Key",
                table: "Permissions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId",
                table: "RolePermissions",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId_RoleId",
                table: "UserRoles",
                columns: new[] { "UserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
