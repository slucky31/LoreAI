using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingQueueTagTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadingQueueTaggedAtUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadingQueueTaggedAtUtc",
                table: "Articles");
        }
    }
}
