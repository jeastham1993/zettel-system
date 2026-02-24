using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZettelWeb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingAndEditorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ContentPieces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftReference",
                table: "ContentPieces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorFeedback",
                table: "ContentPieces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratedTags",
                table: "ContentPieces",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTime>(
                name: "SentToDraftAt",
                table: "ContentPieces",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ContentPieces");

            migrationBuilder.DropColumn(
                name: "DraftReference",
                table: "ContentPieces");

            migrationBuilder.DropColumn(
                name: "EditorFeedback",
                table: "ContentPieces");

            migrationBuilder.DropColumn(
                name: "GeneratedTags",
                table: "ContentPieces");

            migrationBuilder.DropColumn(
                name: "SentToDraftAt",
                table: "ContentPieces");
        }
    }
}
