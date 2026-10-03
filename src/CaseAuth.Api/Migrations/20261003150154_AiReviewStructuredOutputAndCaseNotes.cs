using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseAuth.Api.Migrations
{
    /// <inheritdoc />
    public partial class AiReviewStructuredOutputAndCaseNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Postgres type names, as in InitialCreate: deployment runs on Postgres, and SQLite
            // accepts any type name, so the same migration works locally too.
            migrationBuilder.AddColumn<string>(
                name: "DraftCaseNote",
                table: "AiReviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyConcerns",
                table: "AiReviews",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "NextSteps",
                table: "AiReviews",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "AiReviews",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CaseNotes",
                columns: table => new
                {
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    BasedOnAiReviewVersion = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseNotes", x => x.CaseId);
                    table.ForeignKey(
                        name: "FK_CaseNotes_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseNotes");

            migrationBuilder.DropColumn(
                name: "DraftCaseNote",
                table: "AiReviews");

            migrationBuilder.DropColumn(
                name: "KeyConcerns",
                table: "AiReviews");

            migrationBuilder.DropColumn(
                name: "NextSteps",
                table: "AiReviews");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "AiReviews");
        }
    }
}
