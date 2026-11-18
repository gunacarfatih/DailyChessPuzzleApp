using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class RemovePracticeSessionAndAllowMultipleSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate_IsPractice",
                table: "PuzzleGameSessions");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate_IsPractice",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "IsPractice",
                table: "PuzzleGameSessions");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate",
                table: "PuzzleGameSessions",
                columns: new[] { "ClientId", "PuzzleDate" },
                filter: "\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate",
                table: "PuzzleGameSessions",
                columns: new[] { "UserId", "PuzzleDate" },
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate",
                table: "PuzzleGameSessions");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate",
                table: "PuzzleGameSessions");

            migrationBuilder.AddColumn<bool>(
                name: "IsPractice",
                table: "PuzzleGameSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate_IsPractice",
                table: "PuzzleGameSessions",
                columns: new[] { "ClientId", "PuzzleDate", "IsPractice" },
                unique: true,
                filter: "\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate_IsPractice",
                table: "PuzzleGameSessions",
                columns: new[] { "UserId", "PuzzleDate", "IsPractice" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }
    }
}
