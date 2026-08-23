using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryIndexStates",
                columns: table => new
                {
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    ResumePage = table.Column<int>(type: "integer", nullable: true),
                    LastFullPassStartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFullPassCompletedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryIndexStates", x => x.SourceType);
                });

            migrationBuilder.CreateTable(
                name: "LibraryItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    RaindropCollectionId = table.Column<long>(type: "bigint", nullable: true),
                    Broken = table.Column<bool>(type: "boolean", nullable: false),
                    Important = table.Column<bool>(type: "boolean", nullable: false),
                    Cover = table.Column<string>(type: "text", nullable: true),
                    HighlightsJson = table.Column<string>(type: "jsonb", nullable: true),
                    IndexedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItems_Origin",
                table: "LibraryItems",
                column: "Origin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryIndexStates");

            migrationBuilder.DropTable(
                name: "LibraryItems");
        }
    }
}
