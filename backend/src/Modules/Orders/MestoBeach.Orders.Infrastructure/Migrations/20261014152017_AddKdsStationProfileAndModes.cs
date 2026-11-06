using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKdsStationProfileAndModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-037: con estos DEFAULT las estaciones existentes (Cocina, Parrilla, Barra) siguen
            // encendidas en ambos modos y con el flujo completo hasta que Administración diga otra cosa.
            // El modelo EF no declara los default (un HasDefaultValue(true) en bool omitiría un false).
            migrationBuilder.AddColumn<string>(
                name: "active_in",
                table: "kds_stations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"RESTAURANT\",\"NIGHT\"]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "flow_profile",
                table: "kds_stations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "FULL_PASS");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "kds_stations",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active_in",
                table: "kds_stations");

            migrationBuilder.DropColumn(
                name: "flow_profile",
                table: "kds_stations");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "kds_stations");
        }
    }
}
