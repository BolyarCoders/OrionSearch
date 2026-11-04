using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexResearchAdmissionBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_research_runs_created_budget",
                schema: "research",
                table: "research_runs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_job_attempts_started_budget",
                schema: "system",
                table: "job_attempts",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_research_runs_created_budget",
                schema: "research",
                table: "research_runs");

            migrationBuilder.DropIndex(
                name: "idx_job_attempts_started_budget",
                schema: "system",
                table: "job_attempts");
        }
    }
}
