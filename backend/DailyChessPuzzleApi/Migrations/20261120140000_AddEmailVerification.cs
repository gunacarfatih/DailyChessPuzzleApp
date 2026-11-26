using DailyChessPuzzleApi.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DailyChessPuzzleApi.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261120140000_AddEmailVerification")]
public partial class AddEmailVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EmailVerified",
            table: "Users",
            type: "boolean",
            nullable: false,
            defaultValue: true);
        migrationBuilder.AddColumn<string>(
            name: "EmailVerificationCodeHash",
            table: "Users",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "EmailVerificationCodeExpiresAtUtc",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("ALTER TABLE \"Users\" ALTER COLUMN \"EmailVerified\" DROP DEFAULT;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("EmailVerified", "Users");
        migrationBuilder.DropColumn("EmailVerificationCodeHash", "Users");
        migrationBuilder.DropColumn("EmailVerificationCodeExpiresAtUtc", "Users");
    }
}
