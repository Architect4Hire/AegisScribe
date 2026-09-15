using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RosterEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantRankId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OfficerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RosterEntries_TenantRanks_TenantRankId",
                        column: x => x.TenantRankId,
                        principalTable: "TenantRanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_CharacterId",
                table: "RosterEntries",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TenantId_CharacterId",
                table: "RosterEntries",
                columns: new[] { "TenantId", "CharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TenantId_TenantRankId",
                table: "RosterEntries",
                columns: new[] { "TenantId", "TenantRankId" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TenantRankId",
                table: "RosterEntries",
                column: "TenantRankId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterEntries");
        }
    }
}
