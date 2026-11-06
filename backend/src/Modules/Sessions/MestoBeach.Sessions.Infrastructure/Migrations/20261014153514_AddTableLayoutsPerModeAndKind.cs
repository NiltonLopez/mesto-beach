using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Sessions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTableLayoutsPerModeAndKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "tables",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "TABLE");

            migrationBuilder.AddColumn<string>(
                name: "visible_in",
                table: "floors",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"RESTAURANT\",\"NIGHT\"]'::jsonb");

            migrationBuilder.CreateTable(
                name: "table_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    floor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pos_x = table.Column<double>(type: "double precision", nullable: true),
                    pos_y = table.Column<double>(type: "double precision", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_layouts", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_layouts_floors_floor_id",
                        column: x => x.floor_id,
                        principalTable: "floors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_table_layouts_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_table_layouts_floor_id",
                table: "table_layouts",
                column: "floor_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_layouts_table_id_mode",
                table: "table_layouts",
                columns: new[] { "table_id", "mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_layouts_tenant_id_mode",
                table: "table_layouts",
                columns: new[] { "tenant_id", "mode" });

            // Copia de datos (SDD-003 §Migraciones): por cada mesa, una fila RESTAURANT con su
            // floor_id/pos_x/pos_y actuales y un clon NIGHT idéntico (active = true, station_id = null),
            // para que el plano de noche arranque igual que el de día. active = true en las dos filas:
            // el interruptor global sigue en tables.active. Un floor_id que apunte a un piso ya borrado
            // (no había FK) se copia como null para respetar la FK nueva.
            // tables.floor_id/pos_x/pos_y NO se borran aún (deprecadas; DropLegacyTablePosition).
            migrationBuilder.Sql(@"
INSERT INTO table_layouts (id, tenant_id, table_id, mode, floor_id, pos_x, pos_y, active, station_id, updated_at)
SELECT gen_random_uuid(), t.tenant_id, t.id, m.mode,
       CASE WHEN f.id IS NULL THEN NULL ELSE t.floor_id END,
       t.pos_x, t.pos_y, TRUE, NULL, now()
FROM tables t
CROSS JOIN (VALUES ('RESTAURANT'), ('NIGHT')) AS m(mode)
LEFT JOIN floors f ON f.id = t.floor_id
ON CONFLICT (table_id, mode) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "table_layouts");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "visible_in",
                table: "floors");
        }
    }
}
