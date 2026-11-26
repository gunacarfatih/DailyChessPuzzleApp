
using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DailyChessPuzzleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndAlignLeaderboardSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardCacheOutbox_PuzzleAttempts_PuzzleAttemptId",
                table: "LeaderboardCacheOutbox");

            // PostgreSQL'deki gerçek constraint adı.
            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardCacheOutboxDeliveries_LeaderboardCacheOutbox_Outb",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleAttempts_ClientId_PuzzleId",
                table: "PuzzleAttempts");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_Period_PeriodKey",
                table: "LeaderboardCacheOutboxDeliveries");

            // PostgreSQL'deki gerçek indeks adı.
            migrationBuilder.DropIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_Period_PeriodKey_C",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LeaderboardCacheOutbox",
                table: "LeaderboardCacheOutbox");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardCacheOutbox_PuzzleAttemptId",
                table: "LeaderboardCacheOutbox");

            // Outbox tablosunu yeniden adlandır.
            migrationBuilder.RenameTable(
                name: "LeaderboardCacheOutbox",
                newName: "LeaderboardCacheOutboxes");

            migrationBuilder.RenameIndex(
                name: "IX_LeaderboardCacheOutbox_CreatedAtUtc",
                table: "LeaderboardCacheOutboxes",
                newName: "IX_LeaderboardCacheOutboxes_CreatedAtUtc");

            // PuzzleAttempts.ClientId nullable hale getir.
            migrationBuilder.AlterColumn<string>(
                name: "ClientId",
                table: "PuzzleAttempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "PuzzleAttempts",
                type: "integer",
                nullable: true);

            // LeaderboardScores.ClientId uzunluğunu artır.
            migrationBuilder.AlterColumn<string>(
                name: "ClientId",
                table: "LeaderboardScores",
                type: "character varying(105)",
                maxLength: 105,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            // Eski delivery kayıtları normal güncelleme kabul edilir.
            migrationBuilder.AddColumn<bool>(
                name: "IsRemoval",
                table: "LeaderboardCacheOutboxDeliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // TargetClientId önce nullable eklenir.
            migrationBuilder.AddColumn<string>(
                name: "TargetClientId",
                table: "LeaderboardCacheOutboxDeliveries",
                type: "character varying(105)",
                maxLength: 105,
                nullable: true);

            // Mevcut delivery kayıtlarının hedef kimliğini Outbox'tan al.
            migrationBuilder.Sql("""
                UPDATE "LeaderboardCacheOutboxDeliveries" AS d
                SET "TargetClientId" = o."ClientId"
                FROM "LeaderboardCacheOutboxes" AS o
                WHERE d."OutboxId" = o."Id";
                """);

            // Hedef kimliği olmayan kayıt varsa migration'ın sessizce
            // yanlış veri üretmesini engelle.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "LeaderboardCacheOutboxDeliveries"
                        WHERE "TargetClientId" IS NULL
                    ) THEN
                        RAISE EXCEPTION
                            'TargetClientId backfill failed. Some delivery records have no matching Outbox.';
                    END IF;
                END $$;
                """);

            // Backfill sonrasında alanı zorunlu hale getir.
            migrationBuilder.AlterColumn<string>(
                name: "TargetClientId",
                table: "LeaderboardCacheOutboxDeliveries",
                type: "character varying(105)",
                maxLength: 105,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(105)",
                oldMaxLength: 105,
                oldNullable: true);

            // Outbox ilişkisini nullable hale getir.
            migrationBuilder.AlterColumn<int>(
                name: "PuzzleAttemptId",
                table: "LeaderboardCacheOutboxes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ClientId",
                table: "LeaderboardCacheOutboxes",
                type: "character varying(105)",
                maxLength: 105,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LeaderboardCacheOutboxes",
                table: "LeaderboardCacheOutboxes",
                column: "Id");

            // Users tablosu.
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(
                            type: "integer",
                            nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),

                    Username = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false),

                    Email = table.Column<string>(
                        type: "character varying(254)",
                        maxLength: 254,
                        nullable: false),

                    PasswordHash = table.Column<string>(
                        type: "text",
                        nullable: false),

                    CountryCode = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: true),

                    CreatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),

                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            // Misafir kullanıcıların aynı bulmacayı tekrar çözmesini engelle.
            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_ClientId_PuzzleId",
                table: "PuzzleAttempts",
                columns: new[] { "ClientId", "PuzzleId" },
                unique: true,
                filter: "\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL");

            // Kayıtlı kullanıcıların aynı bulmacayı tekrar çözmesini engelle.
            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_UserId_PuzzleId",
                table: "PuzzleAttempts",
                columns: new[] { "UserId", "PuzzleId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            // CountryCode dolu delivery kayıtları.
            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_TargetClientId_I~1",
                table: "LeaderboardCacheOutboxDeliveries",
                columns: new[]
                {
                    "OutboxId",
                    "TargetClientId",
                    "IsRemoval",
                    "Period",
                    "PeriodKey",
                    "CountryCode"
                },
                unique: true,
                filter: "\"CountryCode\" IS NOT NULL");

            // CountryCode NULL delivery kayıtları.
            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_TargetClientId_Is~",
                table: "LeaderboardCacheOutboxDeliveries",
                columns: new[]
                {
                    "OutboxId",
                    "TargetClientId",
                    "IsRemoval",
                    "Period",
                    "PeriodKey"
                },
                unique: true,
                filter: "\"CountryCode\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCacheOutboxes_PuzzleAttemptId",
                table: "LeaderboardCacheOutboxes",
                column: "PuzzleAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            // Delivery -> Outbox
            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardCacheOutboxDeliveries_LeaderboardCacheOutboxes_O~",
                table: "LeaderboardCacheOutboxDeliveries",
                column: "OutboxId",
                principalTable: "LeaderboardCacheOutboxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Outbox -> PuzzleAttempt (nullable).
            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardCacheOutboxes_PuzzleAttempts_PuzzleAttemptId",
                table: "LeaderboardCacheOutboxes",
                column: "PuzzleAttemptId",
                principalTable: "PuzzleAttempts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // PuzzleAttempt -> User.
            migrationBuilder.AddForeignKey(
                name: "FK_PuzzleAttempts_Users_UserId",
                table: "PuzzleAttempts",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardCacheOutboxDeliveries_LeaderboardCacheOutboxes_O~",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardCacheOutboxes_PuzzleAttempts_PuzzleAttemptId",
                table: "LeaderboardCacheOutboxes");

            migrationBuilder.DropForeignKey(
                name: "FK_PuzzleAttempts_Users_UserId",
                table: "PuzzleAttempts");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleAttempts_ClientId_PuzzleId",
                table: "PuzzleAttempts");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleAttempts_UserId_PuzzleId",
                table: "PuzzleAttempts");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_TargetClientId_I~1",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_TargetClientId_Is~",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LeaderboardCacheOutboxes",
                table: "LeaderboardCacheOutboxes");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardCacheOutboxes_PuzzleAttemptId",
                table: "LeaderboardCacheOutboxes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "IsRemoval",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.DropColumn(
                name: "TargetClientId",
                table: "LeaderboardCacheOutboxDeliveries");

            migrationBuilder.RenameTable(
                name: "LeaderboardCacheOutboxes",
                newName: "LeaderboardCacheOutbox");

            migrationBuilder.RenameIndex(
                name: "IX_LeaderboardCacheOutboxes_CreatedAtUtc",
                table: "LeaderboardCacheOutbox",
                newName: "IX_LeaderboardCacheOutbox_CreatedAtUtc");

            migrationBuilder.AlterColumn<string>(
                name: "ClientId",
                table: "PuzzleAttempts",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientId",
                table: "LeaderboardScores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(105)",
                oldMaxLength: 105);

            migrationBuilder.AlterColumn<int>(
                name: "PuzzleAttemptId",
                table: "LeaderboardCacheOutbox",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientId",
                table: "LeaderboardCacheOutbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(105)",
                oldMaxLength: 105);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LeaderboardCacheOutbox",
                table: "LeaderboardCacheOutbox",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_ClientId_PuzzleId",
                table: "PuzzleAttempts",
                columns: new[] { "ClientId", "PuzzleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_Period_PeriodKey",
                table: "LeaderboardCacheOutboxDeliveries",
                columns: new[] { "OutboxId", "Period", "PeriodKey" },
                unique: true,
                filter: "\"CountryCode\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCacheOutboxDeliveries_OutboxId_Period_PeriodKey_C",
                table: "LeaderboardCacheOutboxDeliveries",
                columns: new[]
                {
                    "OutboxId",
                    "Period",
                    "PeriodKey",
                    "CountryCode"
                },
                unique: true,
                filter: "\"CountryCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCacheOutbox_PuzzleAttemptId",
                table: "LeaderboardCacheOutbox",
                column: "PuzzleAttemptId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardCacheOutbox_PuzzleAttempts_PuzzleAttemptId",
                table: "LeaderboardCacheOutbox",
                column: "PuzzleAttemptId",
                principalTable: "PuzzleAttempts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Eski PostgreSQL constraint adı yeniden oluşturuluyor.
            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardCacheOutboxDeliveries_LeaderboardCacheOutbox_Outb",
                table: "LeaderboardCacheOutboxDeliveries",
                column: "OutboxId",
                principalTable: "LeaderboardCacheOutbox",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
