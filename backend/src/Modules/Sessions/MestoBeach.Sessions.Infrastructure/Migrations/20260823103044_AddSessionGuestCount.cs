using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionGuestCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "guest_count",
                table: "sessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guest_count",
                table: "sessions");
        }
    }
}
