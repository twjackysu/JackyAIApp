using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JackyAIApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomConnectorConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomClientId",
                table: "UserConnectors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomScopes",
                table: "UserConnectors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomTenantId",
                table: "UserConnectors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCustomClientSecret",
                table: "UserConnectors",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomClientId",
                table: "UserConnectors");

            migrationBuilder.DropColumn(
                name: "CustomScopes",
                table: "UserConnectors");

            migrationBuilder.DropColumn(
                name: "CustomTenantId",
                table: "UserConnectors");

            migrationBuilder.DropColumn(
                name: "EncryptedCustomClientSecret",
                table: "UserConnectors");
        }
    }
}
