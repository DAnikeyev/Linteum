using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linteum.Api.Migrations
{
    public partial class SplitSandboxIntoNormalAndFreeDraw : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Canvases\" SET \"CanvasMode\" = 1 WHERE \"CanvasMode\" = 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Canvases\" SET \"CanvasMode\" = 1 WHERE \"CanvasMode\" = 3;");
        }
    }
}

