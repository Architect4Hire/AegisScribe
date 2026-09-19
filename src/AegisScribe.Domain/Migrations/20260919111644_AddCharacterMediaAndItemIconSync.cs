using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterMediaAndItemIconSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IconSyncedAt",
                table: "EquippedItems",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Characters",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MediaSyncedAt",
                table: "Characters",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenderUrl",
                table: "Characters",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquippedItems_BlizzardItemId",
                table: "EquippedItems",
                column: "BlizzardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_MediaSyncedAt",
                table: "Characters",
                column: "MediaSyncedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquippedItems_BlizzardItemId",
                table: "EquippedItems");

            migrationBuilder.DropIndex(
                name: "IX_Characters_MediaSyncedAt",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "IconSyncedAt",
                table: "EquippedItems");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "MediaSyncedAt",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "RenderUrl",
                table: "Characters");
        }
    }
}
