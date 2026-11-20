using DailyChessPuzzleApi.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyChessPuzzleApi.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261120130000_AddLeaderboardUserForeignKeys")]
public partial class AddLeaderboardUserForeignKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "UserId",
            table: "LeaderboardScores",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "UserId",
            table: "LeaderboardCacheOutboxes",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "TargetUserId",
            table: "LeaderboardCacheOutboxDeliveries",
            type: "integer",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "LeaderboardScores" s
            SET "UserId" = u."Id"
            FROM "Users" u
            WHERE s."ClientId" = 'user:' || u."Id"::text;

            UPDATE "LeaderboardCacheOutboxes" o
            SET "UserId" = u."Id"
            FROM "Users" u
            WHERE o."ClientId" = 'user:' || u."Id"::text;

            UPDATE "LeaderboardCacheOutboxDeliveries" d
            SET "TargetUserId" = u."Id"
            FROM "Users" u
            WHERE d."TargetClientId" = 'user:' || u."Id"::text;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_LeaderboardScores_UserId_Period_PeriodKey_CountryCode",
            table: "LeaderboardScores",
            columns: new[] { "UserId", "Period", "PeriodKey", "CountryCode" });
        migrationBuilder.CreateIndex(
            name: "IX_LeaderboardCacheOutboxes_UserId",
            table: "LeaderboardCacheOutboxes",
            column: "UserId");
        migrationBuilder.CreateIndex(
            name: "IX_LeaderboardCacheOutboxDeliveries_TargetUserId",
            table: "LeaderboardCacheOutboxDeliveries",
            column: "TargetUserId");

        migrationBuilder.AddForeignKey(
            name: "FK_LeaderboardScores_Users_UserId",
            table: "LeaderboardScores",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey(
            name: "FK_LeaderboardCacheOutboxes_Users_UserId",
            table: "LeaderboardCacheOutboxes",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey(
            name: "FK_LeaderboardCacheOutboxDeliveries_Users_TargetUserId",
            table: "LeaderboardCacheOutboxDeliveries",
            column: "TargetUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_LeaderboardScores_Users_UserId", "LeaderboardScores");
        migrationBuilder.DropForeignKey("FK_LeaderboardCacheOutboxes_Users_UserId", "LeaderboardCacheOutboxes");
        migrationBuilder.DropForeignKey("FK_LeaderboardCacheOutboxDeliveries_Users_TargetUserId", "LeaderboardCacheOutboxDeliveries");
        migrationBuilder.DropIndex("IX_LeaderboardScores_UserId_Period_PeriodKey_CountryCode", "LeaderboardScores");
        migrationBuilder.DropIndex("IX_LeaderboardCacheOutboxes_UserId", "LeaderboardCacheOutboxes");
        migrationBuilder.DropIndex("IX_LeaderboardCacheOutboxDeliveries_TargetUserId", "LeaderboardCacheOutboxDeliveries");
        migrationBuilder.DropColumn("UserId", "LeaderboardScores");
        migrationBuilder.DropColumn("UserId", "LeaderboardCacheOutboxes");
        migrationBuilder.DropColumn("TargetUserId", "LeaderboardCacheOutboxDeliveries");
    }
}
