using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZettelWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentGenerator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentGenerations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    SeedNoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    ClusterNoteIds = table.Column<string>(type: "jsonb", nullable: false),
                    TopicSummary = table.Column<string>(type: "text", nullable: false),
                    TopicEmbedding = table.Column<float[]>(type: "real[]", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentGenerations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsedSeedNotes",
                columns: table => new
                {
                    NoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsedSeedNotes", x => x.NoteId);
                });

            migrationBuilder.CreateTable(
                name: "VoiceConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Medium = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StyleNotes = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoiceExamples",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Medium = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceExamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPieces",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    GenerationId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Medium = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPieces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPieces_ContentGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "ContentGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPieces_GenerationId",
                table: "ContentPieces",
                column: "GenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPieces_Status",
                table: "ContentPieces",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceConfigs_Medium",
                table: "VoiceConfigs",
                column: "Medium");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceExamples_Medium",
                table: "VoiceExamples",
                column: "Medium");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentPieces");

            migrationBuilder.DropTable(
                name: "UsedSeedNotes");

            migrationBuilder.DropTable(
                name: "VoiceConfigs");

            migrationBuilder.DropTable(
                name: "VoiceExamples");

            migrationBuilder.DropTable(
                name: "ContentGenerations");
        }
    }
}
