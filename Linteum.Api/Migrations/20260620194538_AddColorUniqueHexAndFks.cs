using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Linteum.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddColorUniqueHexAndFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The steps below make this migration safe to apply against a live database whose
            // data may predate these constraints. All of this runs inside the single
            // transaction EF wraps the migration in, so a failure at any step rolls everything
            // back and leaves the database untouched.

            // 1. Collapse any duplicate colors, keeping the lowest Id per HexValue, so the
            //    unique index on HexValue (added below) can be created. The seeder already
            //    guards against inserting duplicates, but this protects pre-existing data.
            migrationBuilder.Sql("""
                DELETE FROM "Colors" d
                USING "Colors" k
                WHERE d."HexValue" = k."HexValue"
                  AND d."Id" > k."Id";
                """);

            // 2. Reassign any orphaned color references to the default color (#FFFFFF) so the
            //    foreign keys (added below) can be created. This mirrors the reassignment the
            //    DbSeeder performs when colors are removed from the palette.
            migrationBuilder.Sql("""
                UPDATE "Pixels" p
                SET "ColorId" = dc."Id"
                FROM "Colors" dc
                WHERE dc."HexValue" = '#FFFFFF'
                  AND NOT EXISTS (SELECT 1 FROM "Colors" c WHERE c."Id" = p."ColorId");
                """);

            migrationBuilder.Sql("""
                UPDATE "PixelChangedEvents" e
                SET "OldColorId" = dc."Id"
                FROM "Colors" dc
                WHERE dc."HexValue" = '#FFFFFF'
                  AND NOT EXISTS (SELECT 1 FROM "Colors" c WHERE c."Id" = e."OldColorId");
                """);

            migrationBuilder.Sql("""
                UPDATE "PixelChangedEvents" e
                SET "NewColorId" = dc."Id"
                FROM "Colors" dc
                WHERE dc."HexValue" = '#FFFFFF'
                  AND NOT EXISTS (SELECT 1 FROM "Colors" c WHERE c."Id" = e."NewColorId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Pixels_ColorId",
                table: "Pixels",
                column: "ColorId");

            migrationBuilder.CreateIndex(
                name: "IX_PixelChangedEvents_NewColorId",
                table: "PixelChangedEvents",
                column: "NewColorId");

            migrationBuilder.CreateIndex(
                name: "IX_PixelChangedEvents_OldColorId",
                table: "PixelChangedEvents",
                column: "OldColorId");

            migrationBuilder.CreateIndex(
                name: "IX_Colors_HexValue",
                table: "Colors",
                column: "HexValue",
                unique: true);

            // 3. Add the foreign keys as NOT VALID, then VALIDATE them. Adding with NOT VALID
            //    skips the validation scan while the constraint is created, so it does not
            //    take a long write-blocking lock on the (potentially large) Pixels and
            //    PixelChangedEvents tables; VALIDATE then confirms that existing rows comply
            //    without blocking writes. The data is already clean after step 2.
            migrationBuilder.Sql("""
                ALTER TABLE "Pixels"
                    ADD CONSTRAINT "FK_Pixels_Colors_ColorId"
                    FOREIGN KEY ("ColorId") REFERENCES "Colors" ("Id")
                    ON DELETE RESTRICT
                    NOT VALID;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "Pixels" VALIDATE CONSTRAINT "FK_Pixels_Colors_ColorId";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "PixelChangedEvents"
                    ADD CONSTRAINT "FK_PixelChangedEvents_Colors_NewColorId"
                    FOREIGN KEY ("NewColorId") REFERENCES "Colors" ("Id")
                    ON DELETE RESTRICT
                    NOT VALID;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "PixelChangedEvents" VALIDATE CONSTRAINT "FK_PixelChangedEvents_Colors_NewColorId";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "PixelChangedEvents"
                    ADD CONSTRAINT "FK_PixelChangedEvents_Colors_OldColorId"
                    FOREIGN KEY ("OldColorId") REFERENCES "Colors" ("Id")
                    ON DELETE RESTRICT
                    NOT VALID;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "PixelChangedEvents" VALIDATE CONSTRAINT "FK_PixelChangedEvents_Colors_OldColorId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the constraints/indexes this migration added. The dedupe/reassignment in
            // Up is not reversible, so Down only removes the schema objects.
            migrationBuilder.Sql("""
                ALTER TABLE "PixelChangedEvents" DROP CONSTRAINT IF EXISTS "FK_PixelChangedEvents_Colors_OldColorId";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "PixelChangedEvents" DROP CONSTRAINT IF EXISTS "FK_PixelChangedEvents_Colors_NewColorId";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "Pixels" DROP CONSTRAINT IF EXISTS "FK_Pixels_Colors_ColorId";
                """);

            migrationBuilder.DropIndex(
                name: "IX_Colors_HexValue",
                table: "Colors");

            migrationBuilder.DropIndex(
                name: "IX_PixelChangedEvents_OldColorId",
                table: "PixelChangedEvents");

            migrationBuilder.DropIndex(
                name: "IX_PixelChangedEvents_NewColorId",
                table: "PixelChangedEvents");

            migrationBuilder.DropIndex(
                name: "IX_Pixels_ColorId",
                table: "Pixels");
        }
    }
}
