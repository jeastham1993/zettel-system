using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZettelWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Permanent"),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EnrichmentJson = table.Column<string>(type: "text", nullable: true),
                    EnrichStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "None"),
                    EnrichRetryCount = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: true),
                    EmbedStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    EmbeddingModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EmbedError = table.Column<string>(type: "text", nullable: true),
                    EmbedRetryCount = table.Column<int>(type: "integer", nullable: false),
                    EmbedUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NoteType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Regular"),
                    SourceAuthor = table.Column<string>(type: "text", nullable: true),
                    SourceTitle = table.Column<string>(type: "text", nullable: true),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    SourceYear = table.Column<int>(type: "integer", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteTags",
                columns: table => new
                {
                    NoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTags", x => new { x.NoteId, x.Tag });
                    table.ForeignKey(
                        name: "FK_NoteTags_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoteVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: true),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteVersions_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteVersions_NoteId",
                table: "NoteVersions",
                column: "NoteId");

            // Custom indexes for query performance
            migrationBuilder.CreateIndex(
                name: "idx_notes_status",
                table: "Notes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_notes_notetype",
                table: "Notes",
                column: "NoteType");

            migrationBuilder.CreateIndex(
                name: "idx_notes_created_at",
                table: "Notes",
                column: "CreatedAt",
                descending: [true]);

            migrationBuilder.CreateIndex(
                name: "idx_notetags_tag",
                table: "NoteTags",
                column: "Tag");

            // Partial indexes for background service polling
            migrationBuilder.Sql("""
                CREATE INDEX idx_notes_embed_status
                ON "Notes" ("EmbedStatus")
                WHERE "EmbedStatus" IN ('Pending', 'Failed', 'Stale');
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_notes_enrich_status
                ON "Notes" ("EnrichStatus")
                WHERE "EnrichStatus" IN ('Pending', 'Failed');
                """);

            // Full-text search GIN index
            migrationBuilder.Sql("""
                CREATE INDEX idx_notes_fulltext
                ON "Notes" USING GIN (to_tsvector('english', "Title" || ' ' || "Content"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "idx_notes_fulltext", table: "Notes");
            migrationBuilder.DropIndex(name: "idx_notes_enrich_status", table: "Notes");
            migrationBuilder.DropIndex(name: "idx_notes_embed_status", table: "Notes");
            migrationBuilder.DropIndex(name: "idx_notetags_tag", table: "NoteTags");
            migrationBuilder.DropIndex(name: "idx_notes_created_at", table: "Notes");
            migrationBuilder.DropIndex(name: "idx_notes_notetype", table: "Notes");
            migrationBuilder.DropIndex(name: "idx_notes_status", table: "Notes");

            migrationBuilder.DropTable(
                name: "NoteTags");

            migrationBuilder.DropTable(
                name: "NoteVersions");

            migrationBuilder.DropTable(
                name: "Notes");
        }
    }
}
