using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill: existing orders get updated_at = created_at (no spurious deltas).
            migrationBuilder.Sql("UPDATE orders SET updated_at = created_at;");

            migrationBuilder.CreateIndex(
                name: "IX_orders_tenant_id_updated_at",
                table: "orders",
                columns: new[] { "tenant_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_tenant_id_updated_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "orders");
        }
    }
}
