using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderVenueModeAndPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "venue_mode",
                table: "orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "RESTAURANT");

            migrationBuilder.CreateIndex(
                name: "IX_orders_tenant_id_venue_mode_created_at",
                table: "orders",
                columns: new[] { "tenant_id", "venue_mode", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_tenant_id_venue_mode_created_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "venue_mode",
                table: "orders");
        }
    }
}
