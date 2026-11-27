using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class ScoringSessionTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AdjustedElapsedMilliseconds",
                table: "PuzzleGameSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AnimationCompensationMilliseconds",
                table: "PuzzleGameSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompensatedOpponentMoves",
                table: "PuzzleGameSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "CompletedScore",
                table: "PuzzleGameSessions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextMoveAllowedAtUtc",
                table: "PuzzleGameSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ServerCompensationMilliseconds",
                table: "PuzzleGameSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalElapsedMilliseconds",
                table: "PuzzleGameSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AdjustedElapsedMilliseconds",
                table: "BonusPuzzleAssignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AnimationCompensationMilliseconds",
                table: "BonusPuzzleAssignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompensatedOpponentMoves",
                table: "BonusPuzzleAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextMoveAllowedAtUtc",
                table: "BonusPuzzleAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ServerCompensationMilliseconds",
                table: "BonusPuzzleAssignments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalElapsedMilliseconds",
                table: "BonusPuzzleAssignments",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustedElapsedMilliseconds",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "AnimationCompensationMilliseconds",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "CompensatedOpponentMoves",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "CompletedScore",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "NextMoveAllowedAtUtc",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "ServerCompensationMilliseconds",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "TotalElapsedMilliseconds",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "AdjustedElapsedMilliseconds",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "AnimationCompensationMilliseconds",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "CompensatedOpponentMoves",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "NextMoveAllowedAtUtc",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "ServerCompensationMilliseconds",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "TotalElapsedMilliseconds",
                table: "BonusPuzzleAssignments");
        }
    }
}
