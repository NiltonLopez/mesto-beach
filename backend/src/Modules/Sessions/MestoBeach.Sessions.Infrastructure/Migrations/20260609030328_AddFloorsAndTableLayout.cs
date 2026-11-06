using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFloorsAndTableLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "floor_id",
                table: "tables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pos_x",
                table: "tables",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pos_y",
                table: "tables",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seats",
                table: "tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shape",
                table: "tables",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "floors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    walls = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_floors", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tables_floor_id",
                table: "tables",
                column: "floor_id");

            migrationBuilder.CreateIndex(
                name: "IX_floors_tenant_id",
                table: "floors",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "floors");

            migrationBuilder.DropIndex(
                name: "IX_tables_floor_id",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "floor_id",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "pos_x",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "pos_y",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "seats",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "shape",
                table: "tables");
        }
    }
}
