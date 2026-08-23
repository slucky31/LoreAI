using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiSourceItemModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PollingStates",
                table: "PollingStates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PollingState_SingleRow",
                table: "PollingStates");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "PollingStates");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "Domain",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "RaindropLastUpdateUtc",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "RaindropType",
                table: "Articles");

            migrationBuilder.RenameColumn(
                name: "RaindropCreatedUtc",
                table: "Articles",
                newName: "CapturedAtUtc");

            migrationBuilder.RenameColumn(
                name: "Link",
                table: "Articles",
                newName: "Url");

            migrationBuilder.RenameIndex(
                name: "IX_Articles_RaindropCreatedUtc",
                table: "Articles",
                newName: "IX_Articles_CapturedAtUtc");

            // La ligne existante (Id = 1) devient SourceType = 'Raindrop' avec son curseur préservé, pas
            // réinitialisée : un curseur perdu rejouerait tout l'historique de « Non trié » au cycle
            // suivant (cf. « First-run caveat », CLAUDE.md). D'où un renommage + cast plutôt qu'un
            // drop/recreate de LastRaindropId, et un defaultValue explicite plutôt que vide sur SourceType,
            // seule source ingérée à ce jour.
            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "PollingStates",
                type: "text",
                nullable: false,
                defaultValue: "Raindrop");

            migrationBuilder.RenameColumn(
                name: "LastRaindropId",
                table: "PollingStates",
                newName: "LastSourceItemId");

            migrationBuilder.Sql(
                "ALTER TABLE \"PollingStates\" ALTER COLUMN \"LastSourceItemId\" TYPE text USING \"LastSourceItemId\"::text;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PollingStates",
                table: "PollingStates",
                column: "SourceType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PollingStates",
                table: "PollingStates");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "PollingStates");

            migrationBuilder.Sql(
                "ALTER TABLE \"PollingStates\" ALTER COLUMN \"LastSourceItemId\" TYPE bigint USING NULLIF(\"LastSourceItemId\", '')::bigint;");

            migrationBuilder.RenameColumn(
                name: "LastSourceItemId",
                table: "PollingStates",
                newName: "LastRaindropId");

            migrationBuilder.RenameColumn(
                name: "Url",
                table: "Articles",
                newName: "Link");

            migrationBuilder.RenameColumn(
                name: "CapturedAtUtc",
                table: "Articles",
                newName: "RaindropCreatedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_Articles_CapturedAtUtc",
                table: "Articles",
                newName: "IX_Articles_RaindropCreatedUtc");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "PollingStates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "CollectionId",
                table: "Articles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Domain",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RaindropLastUpdateUtc",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RaindropType",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PollingStates",
                table: "PollingStates",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PollingState_SingleRow",
                table: "PollingStates",
                sql: "\"Id\" = 1");
        }
    }
}
