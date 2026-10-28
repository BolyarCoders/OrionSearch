using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateArchivesAndMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "research");

            migrationBuilder.CreateTable(
                name: "archives",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archives", x => x.id);
                    table.CheckConstraint("ck_archives_status", "status IN ('active', 'paused', 'archived')");
                    table.CheckConstraint("ck_archives_title", "btrim(title) <> ''");
                    table.ForeignKey(
                        name: "FK_archives_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_members",
                schema: "auth",
                columns: table => new
                {
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false, defaultValue: "viewer"),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_members", x => new { x.archive_id, x.user_id });
                    table.CheckConstraint("ck_archive_members_role", "role IN ('owner', 'editor', 'viewer')");
                    table.ForeignKey(
                        name: "FK_archive_members_archives_archive_id",
                        column: x => x.archive_id,
                        principalSchema: "research",
                        principalTable: "archives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_archive_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_settings",
                schema: "research",
                columns: table => new
                {
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: true),
                    date_to = table.Column<DateOnly>(type: "date", nullable: true),
                    languages = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    preferred_domains = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    excluded_domains = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    config = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_settings", x => x.archive_id);
                    table.CheckConstraint("ck_archive_settings_config", "jsonb_typeof(config) = 'object'");
                    table.CheckConstraint("ck_archive_settings_dates", "date_from IS NULL OR date_to IS NULL OR date_from <= date_to");
                    table.ForeignKey(
                        name: "FK_archive_settings_archives_archive_id",
                        column: x => x.archive_id,
                        principalSchema: "research",
                        principalTable: "archives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_archive_members_user",
                schema: "auth",
                table: "archive_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_archive_members_owner",
                schema: "auth",
                table: "archive_members",
                column: "archive_id",
                unique: true,
                filter: "role = 'owner'");

            migrationBuilder.CreateIndex(
                name: "idx_archives_owner",
                schema: "research",
                table: "archives",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_members",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "archive_settings",
                schema: "research");

            migrationBuilder.DropTable(
                name: "archives",
                schema: "research");
        }
    }
}
