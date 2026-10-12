using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueUserWord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows that are the same word in the same user's collection more than once would make
            // the unique index below fail to build, so keep one of each first: the one with the
            // most review progress, then the one added first. Nothing else references a UserWords
            // row, so dropping the rest loses no other data.
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT Id,
           ROW_NUMBER() OVER (
               PARTITION BY UserId, WordId
               ORDER BY ReviewCount DESC, DateAdded ASC, Id ASC) AS rn
    FROM UserWords
)
DELETE FROM UserWords WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);");

            migrationBuilder.DropIndex(
                name: "IX_UserWords_UserId",
                table: "UserWords");

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_UserId_WordId",
                table: "UserWords",
                columns: new[] { "UserId", "WordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserWords_UserId_WordId",
                table: "UserWords");

            migrationBuilder.CreateIndex(
                name: "IX_UserWords_UserId",
                table: "UserWords",
                column: "UserId");
        }
    }
}
