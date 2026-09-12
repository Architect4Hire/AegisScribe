using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalCharacterDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Realms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    BlizzardConnectedRealmId = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Realms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameLower = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Class = table.Column<int>(type: "int", nullable: false),
                    Spec = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ItemLevel = table.Column<int>(type: "int", nullable: false),
                    Faction = table.Column<int>(type: "int", nullable: false),
                    BlizzardCharacterId = table.Column<long>(type: "bigint", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Realms_RealmId",
                        column: x => x.RealmId,
                        principalTable: "Realms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CharacterEquipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterEquipments_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquippedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CharacterEquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    BlizzardItemId = table.Column<long>(type: "bigint", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quality = table.Column<int>(type: "int", nullable: false),
                    ItemLevel = table.Column<int>(type: "int", nullable: false),
                    IconName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquippedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquippedItems_CharacterEquipments_CharacterEquipmentId",
                        column: x => x.CharacterEquipmentId,
                        principalTable: "CharacterEquipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterEquipments_CharacterId",
                table: "CharacterEquipments",
                column: "CharacterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_RealmId_NameLower",
                table: "Characters",
                columns: new[] { "RealmId", "NameLower" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquippedItems_CharacterEquipmentId_Slot",
                table: "EquippedItems",
                columns: new[] { "CharacterEquipmentId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Realms_Region_Slug",
                table: "Realms",
                columns: new[] { "Region", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquippedItems");

            migrationBuilder.DropTable(
                name: "CharacterEquipments");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Realms");
        }
    }
}
