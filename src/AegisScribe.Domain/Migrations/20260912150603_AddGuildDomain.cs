using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RealmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameLower = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Faction = table.Column<int>(type: "int", nullable: false),
                    BlizzardGuildId = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Guilds_Realms_RealmId",
                        column: x => x.RealmId,
                        principalTable: "Realms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuildMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuildId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlizzardRank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildMembers_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GuildMembers_Guilds_GuildId",
                        column: x => x.GuildId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuildMembers_CharacterId",
                table: "GuildMembers",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildMembers_GuildId_CharacterId",
                table: "GuildMembers",
                columns: new[] { "GuildId", "CharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guilds_RealmId_NameLower",
                table: "Guilds",
                columns: new[] { "RealmId", "NameLower" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildMembers");

            migrationBuilder.DropTable(
                name: "Guilds");
        }
    }
}
