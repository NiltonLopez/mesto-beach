using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadyAtToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ready_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ready_at",
                table: "orders");
        }
    }
}
