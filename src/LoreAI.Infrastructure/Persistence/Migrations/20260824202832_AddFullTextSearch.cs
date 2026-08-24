using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace LoreAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
#pragma warning disable CA1861 // tableau généré par le tooling EF Core, exécuté une seule fois (migration)
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "LibraryItems",
                type: "tsvector",
                nullable: false)
                .Annotation("Npgsql:TsVectorConfig", "french")
                .Annotation("Npgsql:TsVectorProperties", new[] { "Title", "Excerpt" });
#pragma warning restore CA1861

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItems_SearchVector",
                table: "LibraryItems",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LibraryItems_SearchVector",
                table: "LibraryItems");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "LibraryItems");
        }
    }
}
