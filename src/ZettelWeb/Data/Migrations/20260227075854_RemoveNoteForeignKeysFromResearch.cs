using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZettelWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNoteForeignKeysFromResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResearchAgendas_Notes_TriggeredFromNoteId",
                table: "ResearchAgendas");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchFindings_Notes_AcceptedFleetingNoteId",
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
                name: "IX_ResearchAgendas_TriggeredFromNoteId",
                table: "ResearchAgendas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ResearchTasks_MotivationNoteId",
                table: "ResearchTasks",
                column: "MotivationNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchFindings_AcceptedFleetingNoteId",
                table: "ResearchFindings",
                column: "AcceptedFleetingNoteId");

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
                name: "FK_ResearchTasks_Notes_MotivationNoteId",
                table: "ResearchTasks",
                column: "MotivationNoteId",
                principalTable: "Notes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
