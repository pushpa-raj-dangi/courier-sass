using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryZones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryZones_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Provinces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provinces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Districts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProvinceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Districts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Districts_Provinces_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocalLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalLevels_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalLevelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wards_LocalLevels_LocalLevelId",
                        column: x => x.LocalLevelId,
                        principalTable: "LocalLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Areas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Areas_Wards_WardId",
                        column: x => x.WardId,
                        principalTable: "Wards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryZoneAreas",
                columns: table => new
                {
                    CoveredAreasId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryZoneId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryZoneAreas", x => new { x.CoveredAreasId, x.DeliveryZoneId });
                    table.ForeignKey(
                        name: "FK_DeliveryZoneAreas_Areas_CoveredAreasId",
                        column: x => x.CoveredAreasId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryZoneAreas_DeliveryZones_DeliveryZoneId",
                        column: x => x.DeliveryZoneId,
                        principalTable: "DeliveryZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Key" },
                values: new object[,]
                {
                    { new Guid("2eea5274-1bfa-96a2-be44-d1e8c18f73b6"), "Read geography reference data", "geography.read" },
                    { new Guid("5203f802-330a-0b12-67ff-5570e277d1f0"), "Read delivery zones", "zone.read" },
                    { new Guid("e7b2b379-bbd2-f5a8-c0c5-654702988968"), "Update delivery zones", "zone.update" },
                    { new Guid("fe18cd79-2ce4-7ce5-2a5f-ce0bb9693610"), "Create delivery zones", "zone.create" }
                });

            migrationBuilder.InsertData(
                table: "Provinces",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c"), "Lumbini" },
                    { new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a"), "Bagmati" },
                    { new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515"), "Madhesh" },
                    { new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135"), "Gandaki" },
                    { new Guid("902e6dbb-c786-86eb-1a20-dfd166025324"), "Sudurpashchim" },
                    { new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702"), "Karnali" },
                    { new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b"), "Koshi" }
                });

            migrationBuilder.InsertData(
                table: "Districts",
                columns: new[] { "Id", "Name", "ProvinceId" },
                values: new object[,]
                {
                    { new Guid("007130f7-df39-d6dc-d981-ee65bdbf3bd8"), "Chitwan", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("00b8b6e0-c375-46bf-86cf-1a37ba17c15f"), "Dhading", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("038a7c84-9ee9-0018-c89e-0db29de6ba15"), "Lalitpur", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("0aa17044-be75-f208-737d-2faec42bb7d4"), "Rautahat", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("0c9d8ab6-c198-d34f-5c5d-f66955785812"), "Eastern Rukum", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("0d8ae957-69ba-8fcd-7c71-f9e0dfa96082"), "Jajarkot", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("0ed4f14a-04eb-202d-00ce-196293cd16f2"), "Parasi", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("0f53c9dc-28f7-f4df-e62c-bee15baec333"), "Ramechhap", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("14fa88ad-20b4-f2f9-cef4-f0c5ae988393"), "Doti", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("16b05377-abaf-7108-103f-c8b073420f25"), "Syangja", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("1dcaca5e-4114-2110-1ce6-1430f673e111"), "Gulmi", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("1f7bbb28-9aae-e15d-dff8-994763eea1b7"), "Sarlahi", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("2288a129-f66b-b198-91bf-4c025f68c16f"), "Saptari", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("23a8da65-9609-f7a0-4f1c-ccea7a1a273c"), "Terhathum", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("2656b871-d313-f2e6-b019-d6b977443481"), "Bajura", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("27010fcc-15bc-5e4e-9245-d1e10bf3c93b"), "Pyuthan", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("29015f49-0c50-ff65-6e46-da98ba72e155"), "Dolpa", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("2d093140-92cf-d798-a998-676b9e8d0253"), "Ilam", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("2d66cdd7-001c-2bbc-9a12-2f4fc36ada06"), "Kaski", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("333f4f9e-7f55-da15-5f2f-8fe379ca10d8"), "Rolpa", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("39817103-daf5-ac1d-e22e-4b7c1cbf0554"), "Humla", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("3f0051a7-1dc7-9f2d-eda7-6dfaa7610a28"), "Sindhupalchok", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("405ad5e7-b5da-1160-b8b0-c4bd7a33986c"), "Nuwakot", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("40919215-cb27-8bda-8ec5-f3f4e6d9a7b4"), "Baglung", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("4251c274-9eef-e598-5f76-ec97674b5e7e"), "Kalikot", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("4afcbaed-dbba-e109-ff3a-d36c0349ddca"), "Makwanpur", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("4d4c70f0-1487-88fd-46f6-fa54f3aebfb2"), "Kathmandu", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("507a3923-7396-dd1d-ffa8-fdad101ea19b"), "Bhaktapur", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("56401cdd-9729-c925-f010-2720749999ff"), "Rasuwa", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("570fa706-ac49-3f2e-dab9-19cdab9edde4"), "Dolakha", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("574a63d3-fc23-3022-8029-1c2f2b135543"), "Kapilvastu", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("588d7bc0-3b84-5a58-3443-7e0aab1e0d55"), "Khotang", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("58f8b699-3400-62b3-2d95-df609f23b348"), "Mugu", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("5aac6a25-e12b-5ebf-2b73-6885729d2f16"), "Bajhang", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("5ed859ac-0280-915e-ba08-d2b98465ea99"), "Bardiya", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("61422dc7-4e83-c476-4c51-7b951ae61a29"), "Palpa", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("628b1578-f4c2-7f2d-2c0c-eeb98694936e"), "Dang", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("660282e2-0b2d-9aa1-a7dd-40821a799fcc"), "Darchula", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("6891b090-e3d5-12a2-f228-3094a71b982d"), "Solukhumbu", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("6df7320f-5712-f018-6e00-18e8b1fdc6ab"), "Okhaldhunga", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("6e4887cd-0824-5006-485a-2af685a33868"), "Kanchanpur", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("8786330d-9b10-9f06-21c0-5b9689e782d3"), "Lamjung", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("884af7cb-246e-fc97-537a-828a044e56be"), "Nawalpur", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("8858c866-5b09-30bc-7b96-3268ca1951e1"), "Sindhuli", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("8bccf440-1138-d772-077e-e9fce3e1e500"), "Jhapa", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("90ab13a6-4022-33e4-adfd-e67f281017dd"), "Surkhet", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("97e9a9c4-1b31-7541-9cfe-c358833aa1fd"), "Mahottari", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("99745062-237c-0074-4b5b-013edbd260da"), "Sunsari", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("9edf9319-c07e-e84c-73a3-527f07e46a8e"), "Dailekh", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("a67441ad-7e2c-d06d-15ef-946ea5b1c7fd"), "Arghakhanchi", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("a8328c32-dd80-5f04-b541-f151069f8494"), "Western Rukum", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("aab1e45c-eb87-4006-62dd-6ffea18c98e0"), "Dhanusha", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("af58daeb-e43f-1586-12b7-81c4a8ecb119"), "Kailali", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("af751949-30fd-490e-1303-da66457d63cd"), "Gorkha", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("b6108dab-f164-900c-8076-7b9f42902eab"), "Tanahun", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("ba75ad98-a6c6-6e9b-e694-d745c422df00"), "Baitadi", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("bbe1ec5a-9614-8b81-858d-c40fd0f7b2c6"), "Mustang", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("cbfe179e-bfe4-fa78-ce28-3c4b485115af"), "Siraha", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("ccabca5a-8cf1-37af-5d07-925d836a8e21"), "Banke", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("d1d5b685-15a0-5577-df89-d9c37d7cc952"), "Bhojpur", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("d2f14631-0e4c-5596-71b1-68627b35d578"), "Panchthar", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("d4289155-ae50-f795-534f-d8a178d3f97e"), "Morang", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("d5216571-fa41-83b9-ac02-f934c6c0c591"), "Myagdi", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("d540ab14-8c4f-9113-cc8a-bf9e47a66f4f"), "Udayapur", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("d5aa47b8-dcfc-1641-0fd7-65b55517e01a"), "Dhankuta", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("d63cfd9f-0361-3e63-bc30-008fb743da6a"), "Dadeldhura", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("dc533961-3558-715c-628a-18724cc662a3"), "Taplejung", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("dd75a9fb-239b-234f-5761-c30443cf02d4"), "Kavrepalanchok", new Guid("59f5c8d7-fb6a-941a-8d64-642885e9076a") },
                    { new Guid("e077c19d-7d5f-3c62-be1d-cd7808eace1b"), "Rupandehi", new Guid("1fc4c078-0dcc-0c03-3f79-c96a4d405c7c") },
                    { new Guid("e122a137-23c6-177c-a5cc-4277f637253a"), "Jumla", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") },
                    { new Guid("e8990d78-a554-5fe7-c482-d9af2d6829c3"), "Bara", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("eae20588-cb26-3ee4-b97f-14cb7c6f4c9d"), "Parsa", new Guid("65283d1e-5064-992a-26f9-9bc0af3e2515") },
                    { new Guid("ebb64aa2-789a-5137-d350-2caa389fbcb0"), "Parbat", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("ef68987b-f531-4647-0a2a-5f7721b62fa2"), "Sankhuwasabha", new Guid("f2e47c6e-c17e-fa21-38ed-30484b8d695b") },
                    { new Guid("ef97541c-6911-40e0-42ae-12b03471c689"), "Achham", new Guid("902e6dbb-c786-86eb-1a20-dfd166025324") },
                    { new Guid("f113760f-3792-c589-d11d-3570b8c6ac25"), "Manang", new Guid("78c72d87-a1ec-8954-2250-33ffb8e22135") },
                    { new Guid("f48b393d-0fd1-c753-f2c7-5714f3e91861"), "Salyan", new Guid("c5be3e04-e6fc-36f2-8481-6a89ec227702") }
                });

            migrationBuilder.InsertData(
                table: "LocalLevels",
                columns: new[] { "Id", "DistrictId", "Name", "Type" },
                values: new object[,]
                {
                    { new Guid("22d6aa73-f80e-b3fe-74cd-672b66804e49"), new Guid("2d66cdd7-001c-2bbc-9a12-2f4fc36ada06"), "Pokhara Metropolitan City", 0 },
                    { new Guid("6a2055f9-a63b-7fbb-b373-4cf7571d1dad"), new Guid("038a7c84-9ee9-0018-c89e-0db29de6ba15"), "Lalitpur Metropolitan City", 0 },
                    { new Guid("8a4fcb0f-b87e-dcfd-c1fb-7074ba9e6760"), new Guid("507a3923-7396-dd1d-ffa8-fdad101ea19b"), "Bhaktapur Municipality", 2 },
                    { new Guid("9af1740e-3ba8-827f-91fb-342823b2f14e"), new Guid("4d4c70f0-1487-88fd-46f6-fa54f3aebfb2"), "Kathmandu Metropolitan City", 0 },
                    { new Guid("a84013c7-1c0b-7c6d-84f0-ee024edb2f5e"), new Guid("e077c19d-7d5f-3c62-be1d-cd7808eace1b"), "Butwal Sub-Metropolitan City", 1 }
                });

            migrationBuilder.InsertData(
                table: "Wards",
                columns: new[] { "Id", "LocalLevelId", "Number" },
                values: new object[,]
                {
                    { new Guid("09f35779-2786-e2e7-dbd8-027db40825fa"), new Guid("a84013c7-1c0b-7c6d-84f0-ee024edb2f5e"), 1 },
                    { new Guid("153feabb-79cc-fc0e-1e69-530e39339f17"), new Guid("22d6aa73-f80e-b3fe-74cd-672b66804e49"), 2 },
                    { new Guid("1894d96f-f71b-f28c-7af0-308fe8e8dfa8"), new Guid("8a4fcb0f-b87e-dcfd-c1fb-7074ba9e6760"), 2 },
                    { new Guid("1d5b840c-c0de-b416-1539-73e5b6157fcf"), new Guid("8a4fcb0f-b87e-dcfd-c1fb-7074ba9e6760"), 3 },
                    { new Guid("20d1f25c-41e7-9ea2-6178-e861e26d8601"), new Guid("9af1740e-3ba8-827f-91fb-342823b2f14e"), 2 },
                    { new Guid("21f5fe30-dca7-9aa7-07d6-37841c17a905"), new Guid("6a2055f9-a63b-7fbb-b373-4cf7571d1dad"), 1 },
                    { new Guid("5c200267-6f0a-6475-ed49-3a7227505b87"), new Guid("a84013c7-1c0b-7c6d-84f0-ee024edb2f5e"), 2 },
                    { new Guid("62881b7e-0242-3b63-14f4-5e09839bb665"), new Guid("9af1740e-3ba8-827f-91fb-342823b2f14e"), 3 },
                    { new Guid("658c7166-8866-8879-59fd-0e74fe99f087"), new Guid("a84013c7-1c0b-7c6d-84f0-ee024edb2f5e"), 3 },
                    { new Guid("6b20ff37-5983-d2b5-447c-e1ba5d2bb29e"), new Guid("8a4fcb0f-b87e-dcfd-c1fb-7074ba9e6760"), 1 },
                    { new Guid("9b15caae-1076-edd6-97a8-36f36689edad"), new Guid("22d6aa73-f80e-b3fe-74cd-672b66804e49"), 1 },
                    { new Guid("a7812273-5557-1449-04d9-fd93f14532fe"), new Guid("22d6aa73-f80e-b3fe-74cd-672b66804e49"), 3 },
                    { new Guid("b19cfc20-25cb-785c-ca12-066f7a68b6ef"), new Guid("6a2055f9-a63b-7fbb-b373-4cf7571d1dad"), 3 },
                    { new Guid("c510114f-e5ea-e18f-20e3-2342bb55bcb4"), new Guid("6a2055f9-a63b-7fbb-b373-4cf7571d1dad"), 2 },
                    { new Guid("d67b8356-13d5-6c67-2744-fdb6c1ba803d"), new Guid("9af1740e-3ba8-827f-91fb-342823b2f14e"), 1 }
                });

            migrationBuilder.InsertData(
                table: "Areas",
                columns: new[] { "Id", "Name", "WardId" },
                values: new object[,]
                {
                    { new Guid("10d5785f-affc-dc37-f75c-ca83594b7e27"), "Butwal Sub-Metropolitan City Ward 1 Area B", new Guid("09f35779-2786-e2e7-dbd8-027db40825fa") },
                    { new Guid("1be866be-d6a9-54cb-5a6d-f4039d3aea76"), "Pokhara Metropolitan City Ward 2 Area A", new Guid("153feabb-79cc-fc0e-1e69-530e39339f17") },
                    { new Guid("1cc28189-5e03-9543-cbfc-9d9268218553"), "Bhaktapur Municipality Ward 2 Area B", new Guid("1894d96f-f71b-f28c-7af0-308fe8e8dfa8") },
                    { new Guid("2e7f8241-a2e6-cebf-1c3b-9db14b12e147"), "Kathmandu Metropolitan City Ward 2 Area A", new Guid("20d1f25c-41e7-9ea2-6178-e861e26d8601") },
                    { new Guid("3278739a-0ec6-d37c-3efa-64565c727274"), "Lalitpur Metropolitan City Ward 1 Area A", new Guid("21f5fe30-dca7-9aa7-07d6-37841c17a905") },
                    { new Guid("3341edcc-c16e-772e-df5c-4dc5e6cbb880"), "Lalitpur Metropolitan City Ward 3 Area A", new Guid("b19cfc20-25cb-785c-ca12-066f7a68b6ef") },
                    { new Guid("381aead0-e538-a6ee-62ea-f6fe3096fd77"), "Kathmandu Metropolitan City Ward 1 Area A", new Guid("d67b8356-13d5-6c67-2744-fdb6c1ba803d") },
                    { new Guid("4c491974-365c-874d-a74b-6576ac4bdeb0"), "Lalitpur Metropolitan City Ward 1 Area B", new Guid("21f5fe30-dca7-9aa7-07d6-37841c17a905") },
                    { new Guid("69dc31f2-e423-2506-f994-c8d0fb1a9130"), "Pokhara Metropolitan City Ward 2 Area B", new Guid("153feabb-79cc-fc0e-1e69-530e39339f17") },
                    { new Guid("6c30d3f8-1a25-fa3d-ea23-7a4d859696d9"), "Butwal Sub-Metropolitan City Ward 3 Area A", new Guid("658c7166-8866-8879-59fd-0e74fe99f087") },
                    { new Guid("6f962d2f-ebba-9d72-be39-d511eb8591df"), "Bhaktapur Municipality Ward 3 Area A", new Guid("1d5b840c-c0de-b416-1539-73e5b6157fcf") },
                    { new Guid("749c3c69-d7fe-a0a7-871e-c5661e4d20cf"), "Bhaktapur Municipality Ward 3 Area B", new Guid("1d5b840c-c0de-b416-1539-73e5b6157fcf") },
                    { new Guid("75df0034-d95a-8464-ccad-5bac9f69243a"), "Butwal Sub-Metropolitan City Ward 2 Area B", new Guid("5c200267-6f0a-6475-ed49-3a7227505b87") },
                    { new Guid("7c1faff1-f5bd-9ffb-2764-90ba91173c57"), "Kathmandu Metropolitan City Ward 3 Area A", new Guid("62881b7e-0242-3b63-14f4-5e09839bb665") },
                    { new Guid("89d25841-ab97-221c-ab46-7e4619607a76"), "Lalitpur Metropolitan City Ward 3 Area B", new Guid("b19cfc20-25cb-785c-ca12-066f7a68b6ef") },
                    { new Guid("8b20ccc1-d6d2-835b-bdb8-35c9fe7cab59"), "Pokhara Metropolitan City Ward 1 Area B", new Guid("9b15caae-1076-edd6-97a8-36f36689edad") },
                    { new Guid("9669c08f-4e94-4565-0c6a-56502c363c33"), "Kathmandu Metropolitan City Ward 1 Area B", new Guid("d67b8356-13d5-6c67-2744-fdb6c1ba803d") },
                    { new Guid("96b70121-d5ce-6bcf-c8d8-58d6dd2f0aff"), "Kathmandu Metropolitan City Ward 2 Area B", new Guid("20d1f25c-41e7-9ea2-6178-e861e26d8601") },
                    { new Guid("996140f0-d3e5-c0b0-ff74-a74e2e4d4da9"), "Bhaktapur Municipality Ward 1 Area B", new Guid("6b20ff37-5983-d2b5-447c-e1ba5d2bb29e") },
                    { new Guid("9d2ea7ad-f35b-be25-31b4-b2413f9d3806"), "Lalitpur Metropolitan City Ward 2 Area B", new Guid("c510114f-e5ea-e18f-20e3-2342bb55bcb4") },
                    { new Guid("abcae483-b4fd-dfb6-926e-712f035ea894"), "Butwal Sub-Metropolitan City Ward 3 Area B", new Guid("658c7166-8866-8879-59fd-0e74fe99f087") },
                    { new Guid("b3a8faf9-d0fe-1cf3-80ac-b32f1b6f2a84"), "Pokhara Metropolitan City Ward 1 Area A", new Guid("9b15caae-1076-edd6-97a8-36f36689edad") },
                    { new Guid("b893d0cc-bf71-fe9c-1703-74b756ac4b6a"), "Bhaktapur Municipality Ward 2 Area A", new Guid("1894d96f-f71b-f28c-7af0-308fe8e8dfa8") },
                    { new Guid("d6d19fe7-cf7d-bd90-8447-66758abd02b0"), "Lalitpur Metropolitan City Ward 2 Area A", new Guid("c510114f-e5ea-e18f-20e3-2342bb55bcb4") },
                    { new Guid("d7242207-271b-eb1b-d75e-08e9c91813b3"), "Butwal Sub-Metropolitan City Ward 1 Area A", new Guid("09f35779-2786-e2e7-dbd8-027db40825fa") },
                    { new Guid("dd39e5c6-6429-f6a9-3708-58ed37aedf5b"), "Pokhara Metropolitan City Ward 3 Area B", new Guid("a7812273-5557-1449-04d9-fd93f14532fe") },
                    { new Guid("f5828515-1d02-4bf2-fd0c-18ec0591be7e"), "Bhaktapur Municipality Ward 1 Area A", new Guid("6b20ff37-5983-d2b5-447c-e1ba5d2bb29e") },
                    { new Guid("f64c3372-af21-c7b2-4d30-544ef2810524"), "Pokhara Metropolitan City Ward 3 Area A", new Guid("a7812273-5557-1449-04d9-fd93f14532fe") },
                    { new Guid("fccaf4e2-92af-db22-953c-0080713fa711"), "Kathmandu Metropolitan City Ward 3 Area B", new Guid("62881b7e-0242-3b63-14f4-5e09839bb665") },
                    { new Guid("ffb4efc7-37a7-d5af-ead4-6521ac6642f6"), "Butwal Sub-Metropolitan City Ward 2 Area A", new Guid("5c200267-6f0a-6475-ed49-3a7227505b87") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Areas_WardId",
                table: "Areas",
                column: "WardId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryZoneAreas_DeliveryZoneId",
                table: "DeliveryZoneAreas",
                column: "DeliveryZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryZones_OrganizationId",
                table: "DeliveryZones",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_ProvinceId",
                table: "Districts",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_LocalLevels_DistrictId",
                table: "LocalLevels",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Provinces_Name",
                table: "Provinces",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wards_LocalLevelId",
                table: "Wards",
                column: "LocalLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Wards_LocalLevelId_Number",
                table: "Wards",
                columns: new[] { "LocalLevelId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryZoneAreas");

            migrationBuilder.DropTable(
                name: "Areas");

            migrationBuilder.DropTable(
                name: "DeliveryZones");

            migrationBuilder.DropTable(
                name: "Wards");

            migrationBuilder.DropTable(
                name: "LocalLevels");

            migrationBuilder.DropTable(
                name: "Districts");

            migrationBuilder.DropTable(
                name: "Provinces");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("2eea5274-1bfa-96a2-be44-d1e8c18f73b6"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("5203f802-330a-0b12-67ff-5570e277d1f0"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("e7b2b379-bbd2-f5a8-c0c5-654702988968"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("fe18cd79-2ce4-7ce5-2a5f-ce0bb9693610"));
        }
    }
}
