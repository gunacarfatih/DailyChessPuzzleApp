using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBonusPuzzleSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentMoveIndex",
                table: "BonusPuzzleAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "UsedHint",
                table: "BonusPuzzleAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WrongMoves",
                table: "BonusPuzzleAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentMoveIndex",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "UsedHint",
                table: "BonusPuzzleAssignments");

            migrationBuilder.DropColumn(
                name: "WrongMoves",
                table: "BonusPuzzleAssignments");
        }
    }
}
