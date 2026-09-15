using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AegisScribe.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterEntryAltLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MainRosterEntryId",
                table: "RosterEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_MainRosterEntryId",
                table: "RosterEntries",
                column: "MainRosterEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TenantId_MainRosterEntryId",
                table: "RosterEntries",
                columns: new[] { "TenantId", "MainRosterEntryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_RosterEntries_MainRosterEntryId",
                table: "RosterEntries",
                column: "MainRosterEntryId",
                principalTable: "RosterEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_RosterEntries_MainRosterEntryId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_MainRosterEntryId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_TenantId_MainRosterEntryId",
                table: "RosterEntries");

            migrationBuilder.DropColumn(
                name: "MainRosterEntryId",
                table: "RosterEntries");
        }
    }
}
