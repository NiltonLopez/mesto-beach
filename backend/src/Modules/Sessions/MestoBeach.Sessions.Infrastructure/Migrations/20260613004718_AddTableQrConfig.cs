using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTableQrConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "qr_mode",
                table: "tables",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "FIXED");

            migrationBuilder.AddColumn<bool>(
                name: "requires_pin",
                table: "tables",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "qr_mode",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "requires_pin",
                table: "tables");
        }
    }
}
