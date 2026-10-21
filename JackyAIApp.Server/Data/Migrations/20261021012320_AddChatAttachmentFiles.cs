using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAttachmentFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "ChatAttachments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "ChatAttachments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "ChatAttachments",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "ChatAttachments");
        }
    }
}
