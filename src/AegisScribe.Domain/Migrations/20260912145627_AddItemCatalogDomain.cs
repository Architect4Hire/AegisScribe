using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCatalogDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlizzardItemId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quality = table.Column<int>(type: "int", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: true),
                    ItemLevel = table.Column<int>(type: "int", nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Professions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlizzardProfessionId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Professions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlizzardRecipeId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProfessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CraftedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipes_Items_CraftedItemId",
                        column: x => x.CraftedItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recipes_Professions_ProfessionId",
                        column: x => x.ProfessionId,
                        principalTable: "Professions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReagentSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReagentSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReagentSlots_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReagentSlots_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_BlizzardItemId",
                table: "Items",
                column: "BlizzardItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Professions_BlizzardProfessionId",
                table: "Professions",
                column: "BlizzardProfessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReagentSlots_ItemId",
                table: "ReagentSlots",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReagentSlots_RecipeId_ItemId",
                table: "ReagentSlots",
                columns: new[] { "RecipeId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_BlizzardRecipeId",
                table: "Recipes",
                column: "BlizzardRecipeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_CraftedItemId",
                table: "Recipes",
                column: "CraftedItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_ProfessionId",
                table: "Recipes",
                column: "ProfessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReagentSlots");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Professions");
        }
    }
}
