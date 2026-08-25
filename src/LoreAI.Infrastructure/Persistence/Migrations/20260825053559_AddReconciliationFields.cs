using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HumanHandledAtUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAtUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkStatus",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemindedAtUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WriteBackCollectionId",
                table: "Articles",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HumanHandledAtUtc",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "LinkStatus",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "RemindedAtUtc",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "WriteBackCollectionId",
                table: "Articles");
        }
    }
}
