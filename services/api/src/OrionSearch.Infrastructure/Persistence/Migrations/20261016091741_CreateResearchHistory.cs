using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateResearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_runs",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "queued"),
                    trigger_type = table.Column<string>(type: "text", nullable: false, defaultValue: "manual"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    stats = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_research_runs", x => x.id);
                    table.CheckConstraint("ck_research_runs_stats", "jsonb_typeof(stats) = 'object'");
                    table.CheckConstraint("ck_research_runs_status", "status IN ('queued', 'running', 'completed', 'failed', 'cancelled')");
                    table.CheckConstraint("ck_research_runs_times", "started_at IS NULL OR finished_at IS NULL OR finished_at >= started_at");
                    table.CheckConstraint("ck_research_runs_trigger", "trigger_type IN ('manual', 'scheduled', 'system')");
                    table.ForeignKey(
                        name: "FK_research_runs_archives_archive_id",
                        column: x => x.archive_id,
                        principalSchema: "research",
                        principalTable: "archives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "research_queries",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "queued"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_research_queries", x => x.id);
                    table.CheckConstraint("ck_research_queries_provider", "btrim(provider) <> ''");
                    table.CheckConstraint("ck_research_queries_query", "btrim(query) <> ''");
                    table.CheckConstraint("ck_research_queries_status", "status IN ('queued', 'running', 'completed', 'failed')");
                    table.ForeignKey(
                        name: "FK_research_queries_research_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "research",
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_research_queries_run",
                schema: "research",
                table: "research_queries",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "idx_research_runs_archive_created",
                schema: "research",
                table: "research_runs",
                columns: new[] { "archive_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_queries",
                schema: "research");

            migrationBuilder.DropTable(
                name: "research_runs",
                schema: "research");
        }
    }
}
