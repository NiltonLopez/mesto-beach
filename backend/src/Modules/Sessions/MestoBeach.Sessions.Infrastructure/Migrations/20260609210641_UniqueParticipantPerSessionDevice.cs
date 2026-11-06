using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueParticipantPerSessionDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_session_participants_session_id_device_id",
                table: "session_participants");

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_session_id_device_id",
                table: "session_participants",
                columns: new[] { "session_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_session_participants_session_id_device_id",
                table: "session_participants");

            migrationBuilder.CreateIndex(
                name: "IX_session_participants_session_id_device_id",
                table: "session_participants",
                columns: new[] { "session_id", "device_id" });
        }
    }
}
