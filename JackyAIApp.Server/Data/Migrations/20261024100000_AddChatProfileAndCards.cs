using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <summary>
    /// The shared AI chat (docs/ai-native/design/04 §1.4): each conversation names the assistant it
    /// belongs to, every existing one Finance's, the only assistant there was; and an assistant
    /// message can keep the cards its tools showed, so a reopened conversation shows them again; and a
    /// conversation remembers that a turn of it read outside text (a connector's result, a calendar
    /// invite), so later turns' writes ask first too. All three only add columns.
    /// </summary>
    public partial class AddChatProfileAndCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Profile",
                table: "ChatConversations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "finance");

            migrationBuilder.AddColumn<bool>(
                name: "ReadOutsideText",
                table: "ChatConversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CardsJson",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Profile",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ReadOutsideText",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "CardsJson",
                table: "ChatMessages");
        }
    }
}
