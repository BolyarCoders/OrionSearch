using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateJobQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "system");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "system",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    run_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    worker_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_attempts", "attempts >= 0 AND max_attempts > 0 AND attempts <= max_attempts");
                    table.CheckConstraint("ck_jobs_completed", "completed_at IS NULL OR completed_at >= created_at");
                    table.CheckConstraint("ck_jobs_idempotency", "idempotency_key IS NULL OR btrim(idempotency_key) <> ''");
                    table.CheckConstraint("ck_jobs_payload", "jsonb_typeof(payload) = 'object'");
                    table.CheckConstraint("ck_jobs_status", "status IN ('pending', 'processing', 'completed', 'failed', 'cancelled')");
                    table.CheckConstraint("ck_jobs_type", "job_type IN ('research_plan', 'web_search', 'fetch_document', 'extract_content', 'analyze_document', 'generate_embeddings', 'update_knowledge', 'generate_magazine')");
                    table.ForeignKey(
                        name: "FK_jobs_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalSchema: "research",
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_attempts",
                schema: "system",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "running"),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_attempts", x => x.id);
                    table.CheckConstraint("ck_job_attempts_number", "attempt_number > 0");
                    table.CheckConstraint("ck_job_attempts_status", "status IN ('running', 'completed', 'failed', 'timed_out')");
                    table.CheckConstraint("ck_job_attempts_times", "finished_at IS NULL OR finished_at >= started_at");
                    table.ForeignKey(
                        name: "FK_job_attempts_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "system",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_job_attempts_number",
                schema: "system",
                table: "job_attempts",
                columns: new[] { "job_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_jobs_expired_leases",
                schema: "system",
                table: "jobs",
                column: "locked_until",
                filter: "status = 'processing'");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_ready",
                schema: "system",
                table: "jobs",
                columns: new[] { "priority", "created_at" },
                descending: new[] { true, false },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_research_run",
                schema: "system",
                table: "jobs",
                column: "research_run_id");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_run_after",
                schema: "system",
                table: "jobs",
                column: "run_after",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ux_jobs_idempotency",
                schema: "system",
                table: "jobs",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE FUNCTION system.set_updated_at() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.updated_at = statement_timestamp();
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON auth.users
                    FOR EACH ROW EXECUTE FUNCTION system.set_updated_at();
                CREATE TRIGGER trg_archives_updated_at BEFORE UPDATE ON research.archives
                    FOR EACH ROW EXECUTE FUNCTION system.set_updated_at();
                CREATE TRIGGER trg_archive_settings_updated_at BEFORE UPDATE ON research.archive_settings
                    FOR EACH ROW EXECUTE FUNCTION system.set_updated_at();
                CREATE TRIGGER trg_documents_updated_at BEFORE UPDATE ON content.documents
                    FOR EACH ROW EXECUTE FUNCTION system.set_updated_at();
                CREATE TRIGGER trg_jobs_updated_at BEFORE UPDATE ON system.jobs
                    FOR EACH ROW EXECUTE FUNCTION system.set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER trg_users_updated_at ON auth.users;
                DROP TRIGGER trg_archives_updated_at ON research.archives;
                DROP TRIGGER trg_archive_settings_updated_at ON research.archive_settings;
                DROP TRIGGER trg_documents_updated_at ON content.documents;
                DROP TRIGGER trg_jobs_updated_at ON system.jobs;
                DROP FUNCTION system.set_updated_at();
                """);

            migrationBuilder.DropTable(
                name: "job_attempts",
                schema: "system");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "system");
        }
    }
}
