using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DailyChessPuzzleApi.Data;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261120110000_OneActiveDailySessionPerIdentity")]
    public partial class OneActiveDailySessionPerIdentity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "PuzzleGameSessions" older
                SET "IsCompleted" = TRUE,
                    "CompletedAtUtc" = COALESCE(older."CompletedAtUtc", NOW())
                WHERE older."IsCompleted" = FALSE
                  AND older."UserId" IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM "PuzzleGameSessions" newer
                      WHERE newer."UserId" = older."UserId"
                        AND newer."PuzzleDate" = older."PuzzleDate"
                        AND newer."IsCompleted" = FALSE
                        AND newer."Id" > older."Id"
                  );

                UPDATE "PuzzleGameSessions" older
                SET "IsCompleted" = TRUE,
                    "CompletedAtUtc" = COALESCE(older."CompletedAtUtc", NOW())
                WHERE older."IsCompleted" = FALSE
                  AND older."UserId" IS NULL
                  AND older."ClientId" IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM "PuzzleGameSessions" newer
                      WHERE newer."UserId" IS NULL
                        AND newer."ClientId" = older."ClientId"
                        AND newer."PuzzleDate" = older."PuzzleDate"
                        AND newer."IsCompleted" = FALSE
                        AND newer."Id" > older."Id"
                  );
                """);

            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate",
                table: "PuzzleGameSessions");
            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate",
                table: "PuzzleGameSessions");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate",
                table: "PuzzleGameSessions",
                columns: new[] { "ClientId", "PuzzleDate" },
                unique: true,
                filter: "\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL AND \"IsCompleted\" = FALSE");
            migrationBuilder.CreateIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate",
                table: "PuzzleGameSessions",
                columns: new[] { "UserId", "PuzzleDate" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL AND \"IsCompleted\" = FALSE");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_ClientId_PuzzleDate",
                table: "PuzzleGameSessions");
            migrationBuilder.DropIndex(
                name: "IX_PuzzleGameSessions_UserId_PuzzleDate",
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
    }
}
