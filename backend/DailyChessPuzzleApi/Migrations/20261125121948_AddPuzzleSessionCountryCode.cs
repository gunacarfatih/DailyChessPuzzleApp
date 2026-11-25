using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPuzzleSessionCountryCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "PuzzleGameSessions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "BonusPuzzleAssignments",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "PuzzleGameSessions");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "BonusPuzzleAssignments");
        }
    }
}
