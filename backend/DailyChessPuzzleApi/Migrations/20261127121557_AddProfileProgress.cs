using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileProgress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PuzzleAttemptId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    DifficultyBonus = table.Column<int>(type: "integer", nullable: false),
                    WrongMoves = table.Column<int>(type: "integer", nullable: false),
                    UsedHint = table.Column<bool>(type: "boolean", nullable: false),
                    PlayedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileProgress_PuzzleAttempts_PuzzleAttemptId",
                        column: x => x.PuzzleAttemptId,
                        principalTable: "PuzzleAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileProgress_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileProgress_PuzzleAttemptId",
                table: "ProfileProgress",
                column: "PuzzleAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileProgress_UserId_PlayedOn_Kind",
                table: "ProfileProgress",
                columns: new[] { "UserId", "PlayedOn", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileProgress_UserId_PuzzleAttemptId",
                table: "ProfileProgress",
                columns: new[] { "UserId", "PuzzleAttemptId" },
                unique: true);

            // Preserve progression and history for attempts completed before this feature.
            migrationBuilder.Sql("""
                INSERT INTO "ProfileProgress" ("UserId", "PuzzleAttemptId", "Kind", "Xp", "DifficultyBonus", "WrongMoves", "UsedHint", "PlayedOn", "CreatedAtUtc")
                SELECT a."UserId", a."Id",
                       CASE WHEN b."Id" IS NULL THEN 'daily' ELSE 'bonus' END,
                       100 + CASE WHEN p."Rating" >= 2000 THEN 50 WHEN p."Rating" >= 1400 THEN 25 ELSE 0 END
                           + CASE WHEN a."WrongMoves" = 0 THEN 25 ELSE 0 END
                           + CASE WHEN NOT a."UsedHint" THEN 25 ELSE 0 END,
                       CASE WHEN p."Rating" >= 2000 THEN 50 WHEN p."Rating" >= 1400 THEN 25 ELSE 0 END,
                       a."WrongMoves", a."UsedHint", a."PlayedOn", a."SubmittedAtUtc"
                FROM "PuzzleAttempts" a
                JOIN "Puzzles" p ON p."Id" = a."PuzzleId"
                LEFT JOIN "BonusPuzzleAssignments" b ON b."PuzzleAttemptId" = a."Id"
                WHERE a."UserId" IS NOT NULL
                ON CONFLICT ("UserId", "PuzzleAttemptId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileProgress");
        }
    }
}
