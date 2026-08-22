using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Link = table.Column<string>(type: "text", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    OriginalTags = table.Column<string[]>(type: "text[]", nullable: false),
                    CollectionId = table.Column<long>(type: "bigint", nullable: true),
                    Domain = table.Column<string>(type: "text", nullable: true),
                    RaindropType = table.Column<string>(type: "text", nullable: true),
                    RaindropCreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RaindropLastUpdateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SuggestedCollection = table.Column<string>(type: "text", nullable: true),
                    SuggestedTags = table.Column<string[]>(type: "text[]", nullable: false),
                    RecommendedAction = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ClassificationModel = table.Column<string>(type: "text", nullable: true),
                    ClassificationRawResponse = table.Column<string>(type: "jsonb", nullable: true),
                    ClassifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Moved = table.Column<bool>(type: "boolean", nullable: false),
                    WriteBackStatus = table.Column<string>(type: "text", nullable: true),
                    WriteBackAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DiscordNotifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailDigestSentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PollingStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    LastRaindropId = table.Column<long>(type: "bigint", nullable: true),
                    LastCreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollingStates", x => x.Id);
                    table.CheckConstraint("CK_PollingState_SingleRow", "\"Id\" = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_EmailDigestSentAtUtc",
                table: "Articles",
                column: "EmailDigestSentAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_RaindropCreatedUtc",
                table: "Articles",
                column: "RaindropCreatedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Articles");

            migrationBuilder.DropTable(
                name: "PollingStates");
        }
    }
}
