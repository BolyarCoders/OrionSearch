using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "access_failed_count",
                schema: "auth",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "concurrency_stamp",
                schema: "auth",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "email_confirmed",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "lockout_enabled",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_end",
                schema: "auth",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                schema: "auth",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                schema: "auth",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "system",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                schema: "auth",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_access_failed_count",
                schema: "auth",
                table: "users",
                sql: "access_failed_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE auth.users, system.data_protection_keys IN ACCESS EXCLUSIVE MODE;
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM system.data_protection_keys)
                       OR EXISTS (SELECT 1 FROM auth.users WHERE normalized_email IS NOT NULL OR email_confirmed) THEN
                        RAISE EXCEPTION 'Cannot discard enrolled Identity accounts or token-protection keys.' USING ERRCODE = '23514';
                    END IF;
                END
                $rollback$;
                """);
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "system");

            migrationBuilder.DropIndex(
                name: "ux_users_normalized_email",
                schema: "auth",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_access_failed_count",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "access_failed_count",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_confirmed",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_enabled",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                schema: "auth",
                table: "users");
        }
    }
}
