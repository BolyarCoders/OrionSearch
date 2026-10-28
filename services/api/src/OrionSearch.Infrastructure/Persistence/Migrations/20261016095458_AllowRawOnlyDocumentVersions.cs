using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrionSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowRawOnlyDocumentVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "extracted_text",
                schema: "content",
                table: "document_versions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_versions_content_presence",
                schema: "content",
                table: "document_versions",
                sql: "extracted_text IS NOT NULL OR storage_uri IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_versions_raw_hash_metadata",
                schema: "content",
                table: "document_versions",
                sql: "extracted_text IS NOT NULL OR COALESCE(metadata #>> '{content_hash,algorithm}' = 'sha256' AND metadata #>> '{content_hash,basis}' = 'raw_bytes', false)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE content.document_versions IN ACCESS EXCLUSIVE MODE;
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM content.document_versions WHERE extracted_text IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required extracted_text while raw-only document versions exist.'
                            USING ERRCODE = '23514';
                    END IF;
                END
                $rollback$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_versions_content_presence",
                schema: "content",
                table: "document_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_versions_raw_hash_metadata",
                schema: "content",
                table: "document_versions");

            migrationBuilder.AlterColumn<string>(
                name: "extracted_text",
                schema: "content",
                table: "document_versions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
