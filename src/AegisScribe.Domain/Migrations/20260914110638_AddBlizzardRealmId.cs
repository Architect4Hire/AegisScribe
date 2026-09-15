using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddBlizzardRealmId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BlizzardRealmId",
                table: "Realms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Realms_BlizzardRealmId",
                table: "Realms",
                column: "BlizzardRealmId",
                unique: true,
                filter: "[BlizzardRealmId] <> 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Realms_BlizzardRealmId",
                table: "Realms");

            migrationBuilder.DropColumn(
                name: "BlizzardRealmId",
                table: "Realms");
        }
    }
}
