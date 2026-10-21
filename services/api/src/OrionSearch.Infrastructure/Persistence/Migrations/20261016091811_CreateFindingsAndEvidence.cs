using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateFindingsAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "findings",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    statement = table.Column<string>(type: "text", nullable: false),
                    finding_type = table.Column<string>(type: "text", nullable: false, defaultValue: "claim"),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "unverified"),
                    confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    first_observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_findings", x => x.id);
                    table.CheckConstraint("ck_findings_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("ck_findings_statement", "btrim(statement) <> ''");
                    table.CheckConstraint("ck_findings_status", "status IN ('unverified', 'supported', 'disputed', 'superseded')");
                    table.CheckConstraint("ck_findings_type", "finding_type IN ('claim', 'observation', 'estimate')");
                });

            migrationBuilder.CreateTable(
                name: "finding_evidence",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_text = table.Column<string>(type: "text", nullable: true),
                    stance = table.Column<string>(type: "text", nullable: false, defaultValue: "mentions"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finding_evidence", x => x.id);
                    table.CheckConstraint("ck_finding_evidence_stance", "stance IN ('supports', 'contradicts', 'mentions')");
                    table.ForeignKey(
                        name: "FK_finding_evidence_document_versions_document_version_id",
                        column: x => x.document_version_id,
                        principalSchema: "content",
                        principalTable: "document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finding_evidence_findings_finding_id",
                        column: x => x.finding_id,
                        principalSchema: "knowledge",
                        principalTable: "findings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_finding_evidence_chunk_version",
                        columns: x => new { x.chunk_id, x.document_version_id },
                        principalSchema: "content",
                        principalTable: "document_chunks",
                        principalColumns: new[] { "id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_finding_evidence_chunk_version",
                schema: "knowledge",
                table: "finding_evidence",
                columns: new[] { "chunk_id", "document_version_id" });

            migrationBuilder.CreateIndex(
                name: "idx_finding_evidence_finding",
                schema: "knowledge",
                table: "finding_evidence",
                column: "finding_id");

            migrationBuilder.CreateIndex(
                name: "idx_finding_evidence_version",
                schema: "knowledge",
                table: "finding_evidence",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "idx_findings_first_observed",
                schema: "knowledge",
                table: "findings",
                column: "first_observed_at",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finding_evidence",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "findings",
                schema: "knowledge");
        }
    }
}
