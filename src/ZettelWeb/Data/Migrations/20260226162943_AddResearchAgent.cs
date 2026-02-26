using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZettelWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResearchAgendas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    TriggeredFromNoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchAgendas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResearchTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    AgendaId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Query = table.Column<string>(type: "text", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Motivation = table.Column<string>(type: "text", nullable: false),
                    MotivationNoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    BlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchTasks_ResearchAgendas_AgendaId",
                        column: x => x.AgendaId,
                        principalTable: "ResearchAgendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchFindings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    TaskId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Synthesis = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SimilarNoteIds = table.Column<string>(type: "jsonb", nullable: false),
                    DuplicateSimilarity = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    AcceptedFleetingNoteId = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchFindings_ResearchTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ResearchTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchFindings_Status",
                table: "ResearchFindings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchFindings_TaskId",
                table: "ResearchFindings",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchTasks_AgendaId",
                table: "ResearchTasks",
                column: "AgendaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResearchFindings");

            migrationBuilder.DropTable(
                name: "ResearchTasks");

            migrationBuilder.DropTable(
                name: "ResearchAgendas");
        }
    }
}
