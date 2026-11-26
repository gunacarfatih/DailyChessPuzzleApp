using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPuzzleGameSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "StartedAtUtc",
                table: "BonusPuzzleAssignments",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateTable(
                name: "PuzzleGameSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PuzzleId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PuzzleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentMoveIndex = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    WrongMoves = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UsedHint = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuzzleGameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PuzzleGameSessions_Puzzles_PuzzleId",
                        column: x => x.PuzzleId,
                        principalTable: "Puzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PuzzleGameSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate",
                table: "PuzzleGameSessions",
                columns: new[] { "ClientId", "PuzzleDate" },
                unique: true,
                filter: "\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_PuzzleId",
                table: "PuzzleGameSessions",
                column: "PuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate",
                table: "PuzzleGameSessions",
                columns: new[] { "UserId", "PuzzleDate" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PuzzleGameSessions");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartedAtUtc",
                table: "BonusPuzzleAssignments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
