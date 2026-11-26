using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBonusPuzzleAndAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyPuzzles_PuzzleDate",
                table: "DailyPuzzles");

            migrationBuilder.AddColumn<int>(
                name: "Audience",
                table: "DailyPuzzles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BonusPuzzleAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PuzzleId = table.Column<int>(type: "integer", nullable: false),
                    PuzzleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PuzzleAttemptId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BonusPuzzleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BonusPuzzleAssignments_PuzzleAttempts_PuzzleAttemptId",
                        column: x => x.PuzzleAttemptId,
                        principalTable: "PuzzleAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BonusPuzzleAssignments_Puzzles_PuzzleId",
                        column: x => x.PuzzleId,
                        principalTable: "Puzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BonusPuzzleAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Puzzles_IsActive_Rating",
                table: "Puzzles",
                columns: new[] { "IsActive", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyPuzzles_PuzzleDate_Audience",
                table: "DailyPuzzles",
                columns: new[] { "PuzzleDate", "Audience" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BonusPuzzleAssignments_PuzzleAttemptId",
                table: "BonusPuzzleAssignments",
                column: "PuzzleAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_BonusPuzzleAssignments_PuzzleId",
                table: "BonusPuzzleAssignments",
                column: "PuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_BonusPuzzleAssignments_UserId_PuzzleDate",
                table: "BonusPuzzleAssignments",
                columns: new[] { "UserId", "PuzzleDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BonusPuzzleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Puzzles_IsActive_Rating",
                table: "Puzzles");

            migrationBuilder.DropIndex(
                name: "IX_DailyPuzzles_PuzzleDate_Audience",
                table: "DailyPuzzles");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "DailyPuzzles");

            migrationBuilder.CreateIndex(
                name: "IX_DailyPuzzles_PuzzleDate",
                table: "DailyPuzzles",
                column: "PuzzleDate",
                unique: true);
        }
    }
}
