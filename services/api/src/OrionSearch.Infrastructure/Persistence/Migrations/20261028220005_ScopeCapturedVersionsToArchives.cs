using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCapturedVersionsToArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_versions_id_document_id",
                schema: "content",
                table: "document_versions",
                columns: new[] { "id", "document_id" });

            migrationBuilder.CreateTable(
                name: "archive_document_versions",
                schema: "content",
                columns: table => new
                {
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_uri = table.Column<string>(type: "text", nullable: true),
                    raw_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_document_versions", x => new { x.archive_id, x.document_version_id });
                    table.CheckConstraint("ck_archive_versions_raw", "(storage_uri IS NULL AND raw_sha256 IS NULL) OR (storage_uri IS NOT NULL AND btrim(storage_uri) <> '' AND raw_sha256 IS NOT NULL AND raw_sha256 ~ '^[0-9a-f]{64}$')");
                    table.ForeignKey(
                        name: "FK_archive_document_versions_archive_documents_archive_id_docu~",
                        columns: x => new { x.archive_id, x.document_id },
                        principalSchema: "content",
                        principalTable: "archive_documents",
                        principalColumns: new[] { "archive_id", "document_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_archive_document_versions_document_versions_document_versio~",
                        columns: x => new { x.document_version_id, x.document_id },
                        principalSchema: "content",
                        principalTable: "document_versions",
                        principalColumns: new[] { "id", "document_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_archive_versions_version",
                schema: "content",
                table: "archive_document_versions",
                columns: new[] { "document_version_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_archive_document_versions_archive_id_document_id",
                schema: "content",
                table: "archive_document_versions",
                columns: new[] { "archive_id", "document_id" });

            migrationBuilder.Sql("""
                INSERT INTO content.archive_document_versions (archive_id, document_id, document_version_id)
                SELECT DISTINCT r.archive_id, v.document_id, v.id
                FROM knowledge.finding_evidence e
                JOIN knowledge.findings f ON f.id = e.finding_id
                JOIN research.research_runs r ON r.id = f.research_run_id
                JOIN content.document_versions v ON v.id = e.document_version_id
                JOIN content.archive_documents a ON a.archive_id = r.archive_id AND a.document_id = v.document_id
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE content.archive_document_versions IN ACCESS EXCLUSIVE MODE;
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM content.archive_document_versions) THEN
                        RAISE EXCEPTION 'Cannot discard archive/version access grants; roll back the application without dropping the schema.'
                            USING ERRCODE = '23514';
                    END IF;
                END
                $rollback$;
                """);
            migrationBuilder.DropTable(
                name: "archive_document_versions",
                schema: "content");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_versions_id_document_id",
                schema: "content",
                table: "document_versions");
        }
    }
}
