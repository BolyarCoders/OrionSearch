using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementResearchExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "research_run_id",
                schema: "knowledge",
                table: "findings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_jobs_research_plan_run",
                schema: "system",
                table: "jobs",
                column: "research_run_id",
                unique: true,
                filter: "research_run_id IS NOT NULL AND job_type = 'research_plan'");

            migrationBuilder.CreateIndex(
                name: "idx_findings_research_run",
                schema: "knowledge",
                table: "findings",
                column: "research_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_versions_storage_not_blank",
                schema: "content",
                table: "document_versions",
                sql: "storage_uri IS NULL OR btrim(storage_uri, E' \\t\\n\\r\\f' || U&'\\000B\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000') <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_versions_text_not_blank",
                schema: "content",
                table: "document_versions",
                sql: "extracted_text IS NULL OR btrim(extracted_text, E' \\t\\n\\r\\f' || U&'\\000B\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000') <> ''");

            migrationBuilder.AddForeignKey(
                name: "FK_findings_research_runs_research_run_id",
                schema: "knowledge",
                table: "findings",
                column: "research_run_id",
                principalSchema: "research",
                principalTable: "research_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE knowledge.findings IN ACCESS EXCLUSIVE MODE;
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM knowledge.findings WHERE research_run_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot discard research-run provenance from findings.' USING ERRCODE = '23514';
                    END IF;
                END
                $rollback$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_findings_research_runs_research_run_id",
                schema: "knowledge",
                table: "findings");

            migrationBuilder.DropIndex(
                name: "ux_jobs_research_plan_run",
                schema: "system",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "idx_findings_research_run",
                schema: "knowledge",
                table: "findings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_versions_storage_not_blank",
                schema: "content",
                table: "document_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_versions_text_not_blank",
                schema: "content",
                table: "document_versions");

            migrationBuilder.DropColumn(
                name: "research_run_id",
                schema: "knowledge",
                table: "findings");

        }
    }
}
