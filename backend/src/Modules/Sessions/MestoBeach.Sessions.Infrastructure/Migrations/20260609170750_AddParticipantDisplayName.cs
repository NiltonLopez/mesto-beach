using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParticipantDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "session_participants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "session_participants");
        }
    }
}
