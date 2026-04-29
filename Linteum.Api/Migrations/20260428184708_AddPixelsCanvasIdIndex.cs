using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linteum.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPixelsCanvasIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Pixels_CanvasId",
                table: "Pixels",
                column: "CanvasId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pixels_CanvasId",
                table: "Pixels");
        }
    }
}
