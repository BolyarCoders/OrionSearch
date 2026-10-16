using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateContentStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.CreateTable(
                name: "publishers",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    domain = table.Column<string>(type: "text", nullable: false),
                    homepage_url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publishers", x => x.id);
                    table.CheckConstraint("ck_publishers_domain", "domain = lower(domain) AND domain ~ '^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$'");
                    table.CheckConstraint("ck_publishers_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_url = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.CheckConstraint("ck_documents_seen", "last_seen_at >= first_seen_at");
                    table.CheckConstraint("ck_documents_url", "canonical_url ~ '^https?://[^[:space:]]+$'");
                    table.ForeignKey(
                        name: "FK_documents_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalSchema: "content",
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_documents",
                schema: "content",
                columns: table => new
                {
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relevance_score = table.Column<decimal>(type: "numeric", nullable: true),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_documents", x => new { x.archive_id, x.document_id });
                    table.CheckConstraint("ck_archive_documents_relevance", "relevance_score IS NULL OR (relevance_score >= 0 AND relevance_score <= 1)");
                    table.ForeignKey(
                        name: "FK_archive_documents_archives_archive_id",
                        column: x => x.archive_id,
                        principalSchema: "research",
                        principalTable: "archives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_archive_documents_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "content",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    extracted_text = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    storage_uri = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_versions", x => x.id);
                    table.CheckConstraint("ck_document_versions_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_document_versions_metadata", "jsonb_typeof(metadata) = 'object'");
                    table.ForeignKey(
                        name: "FK_document_versions_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "content",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: true),
                    start_offset = table.Column<int>(type: "integer", nullable: true),
                    end_offset = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_chunks", x => x.id);
                    table.UniqueConstraint("ak_document_chunks_id_version", x => new { x.id, x.version_id });
                    table.CheckConstraint("ck_document_chunks_index", "chunk_index >= 0");
                    table.CheckConstraint("ck_document_chunks_offsets", "(start_offset IS NULL AND end_offset IS NULL) OR (start_offset IS NOT NULL AND end_offset IS NOT NULL AND start_offset >= 0 AND end_offset > start_offset)");
                    table.CheckConstraint("ck_document_chunks_tokens", "token_count IS NULL OR token_count >= 0");
                    table.ForeignKey(
                        name: "FK_document_chunks_document_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "content",
                        principalTable: "document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_archive_documents_document",
                schema: "content",
                table: "archive_documents",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "idx_chunks_fts",
                schema: "content",
                table: "document_chunks",
                column: "content")
                .Annotation("Npgsql:IndexMethod", "GIN")
                .Annotation("Npgsql:TsVectorConfig", "simple");

            migrationBuilder.CreateIndex(
                name: "ux_document_chunks_version_index",
                schema: "content",
                table: "document_chunks",
                columns: new[] { "version_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_document_versions_document_fetched",
                schema: "content",
                table: "document_versions",
                columns: new[] { "document_id", "fetched_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_document_versions_content",
                schema: "content",
                table: "document_versions",
                columns: new[] { "document_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_documents_publisher",
                schema: "content",
                table: "documents",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ux_documents_canonical_url",
                schema: "content",
                table: "documents",
                column: "canonical_url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_publishers_domain",
                schema: "content",
                table: "publishers",
                column: "domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_documents",
                schema: "content");

            migrationBuilder.DropTable(
                name: "document_chunks",
                schema: "content");

            migrationBuilder.DropTable(
                name: "document_versions",
                schema: "content");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "content");

            migrationBuilder.DropTable(
                name: "publishers",
                schema: "content");
        }
    }
}
