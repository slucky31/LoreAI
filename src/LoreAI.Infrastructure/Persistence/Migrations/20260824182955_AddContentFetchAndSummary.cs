using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentFetchAndSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentFetchedAtUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentStatus",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentText",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WordCount",
                table: "Articles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentFetchedAtUtc",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ContentStatus",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ContentText",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "WordCount",
                table: "Articles");
        }
    }
}
