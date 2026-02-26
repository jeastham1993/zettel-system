using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZettelWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixResearchAgentReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResearchFindings_ResearchTasks_TaskId",
                table: "ResearchFindings");

            migrationBuilder.DropIndex(
                name: "IX_ResearchFindings_Status",
                table: "ResearchFindings");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchTasks_MotivationNoteId",
                table: "ResearchTasks",
                column: "MotivationNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchFindings_AcceptedFleetingNoteId",
                table: "ResearchFindings",
                column: "AcceptedFleetingNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchFindings_Status_CreatedAt",
                table: "ResearchFindings",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchAgendas_TriggeredFromNoteId",
                table: "ResearchAgendas",
                column: "TriggeredFromNoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchAgendas_Notes_TriggeredFromNoteId",
                table: "ResearchAgendas",
                column: "TriggeredFromNoteId",
                principalTable: "Notes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchFindings_Notes_AcceptedFleetingNoteId",
                table: "ResearchFindings",
                column: "AcceptedFleetingNoteId",
                principalTable: "Notes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchFindings_ResearchTasks_TaskId",
                table: "ResearchFindings",
                column: "TaskId",
                principalTable: "ResearchTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchTasks_Notes_MotivationNoteId",
                table: "ResearchTasks",
                column: "MotivationNoteId",
                principalTable: "Notes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResearchAgendas_Notes_TriggeredFromNoteId",
                table: "ResearchAgendas");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchFindings_Notes_AcceptedFleetingNoteId",
                table: "ResearchFindings");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchFindings_ResearchTasks_TaskId",
                table: "ResearchFindings");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchTasks_Notes_MotivationNoteId",
                table: "ResearchTasks");

            migrationBuilder.DropIndex(
                name: "IX_ResearchTasks_MotivationNoteId",
                table: "ResearchTasks");

            migrationBuilder.DropIndex(
                name: "IX_ResearchFindings_AcceptedFleetingNoteId",
                table: "ResearchFindings");

            migrationBuilder.DropIndex(
                name: "IX_ResearchFindings_Status_CreatedAt",
                table: "ResearchFindings");

            migrationBuilder.DropIndex(
                name: "IX_ResearchAgendas_TriggeredFromNoteId",
                table: "ResearchAgendas");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchFindings_Status",
                table: "ResearchFindings",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchFindings_ResearchTasks_TaskId",
                table: "ResearchFindings",
                column: "TaskId",
                principalTable: "ResearchTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
