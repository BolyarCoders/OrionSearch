using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "report",
                schema: "research",
                table: "research_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_research_runs_report",
                schema: "research",
                table: "research_runs",
                sql: "report IS NULL OR (char_length(report) BETWEEN 1 AND 100000 AND btrim(report) <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM research.research_runs WHERE report IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot remove stored research reports' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_research_runs_report",
                schema: "research",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "report",
                schema: "research",
                table: "research_runs");
        }
    }
}
