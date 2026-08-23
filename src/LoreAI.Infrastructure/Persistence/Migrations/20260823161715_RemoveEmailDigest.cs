using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEmailDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_EmailDigestSentAtUtc",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "EmailDigestSentAtUtc",
                table: "Articles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailDigestSentAtUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Articles_EmailDigestSentAtUtc",
                table: "Articles",
                column: "EmailDigestSentAtUtc");
        }
    }
}
