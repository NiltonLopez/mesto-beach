using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_floor_id",
                table: "user_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_station_id",
                table: "user_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_memberships_tenant_id_assigned_floor_id",
                table: "user_memberships",
                columns: new[] { "tenant_id", "assigned_floor_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_memberships_tenant_id_assigned_station_id",
                table: "user_memberships",
                columns: new[] { "tenant_id", "assigned_station_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_memberships_tenant_id_assigned_floor_id",
                table: "user_memberships");

            migrationBuilder.DropIndex(
                name: "IX_user_memberships_tenant_id_assigned_station_id",
                table: "user_memberships");

            migrationBuilder.DropColumn(
                name: "assigned_floor_id",
                table: "user_memberships");

            migrationBuilder.DropColumn(
                name: "assigned_station_id",
                table: "user_memberships");
        }
    }
}
