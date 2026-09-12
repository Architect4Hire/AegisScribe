using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class MakeCharacterBlizzardIdRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "BlizzardCharacterId",
                table: "Characters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_BlizzardCharacterId",
                table: "Characters",
                column: "BlizzardCharacterId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Characters_BlizzardCharacterId",
                table: "Characters");

            migrationBuilder.AlterColumn<long>(
                name: "BlizzardCharacterId",
                table: "Characters",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
