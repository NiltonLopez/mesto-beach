using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ADR-025: el ciclo de vida baja al plato. Añade a <c>order_items</c> su propio estado,
    /// el coursing ("ahora"/"luego"), la estación por ítem, los sellos de tiempo del recorrido
    /// y el cursor del delta del KDS; y crea <c>delivery_trips</c> (viajes de runner, ADR-027)
    /// con un solo viaje abierto por runner.
    /// </summary>
    public partial class AddItemLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "delivered_at",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delivery_trip_id",
                table: "order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "marched_at",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "marched_by",
                table: "order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ready_at",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "station_id",
                table: "order_items",
                type: "uuid",
                nullable: true);

            // IN_PREPARATION como valor de arranque: es el estado en el que vivían de facto
            // los ítems del modelo v1 (una comanda confirmada iba entera a su estación).
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "order_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "IN_PREPARATION");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "order_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // NOW: en el modelo v1 no había coursing — todo lo pedido se preparaba de inmediato.
            migrationBuilder.AddColumn<string>(
                name: "timing",
                table: "order_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "NOW");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // ── Backfill de los ítems que ya existen ──────────────────────────────────────
            //
            // Un ítem viejo hereda del pedido al que pertenece: tenant, estación y cursor.
            // Su estado se deriva del estado del pedido, que es la única verdad que existía:
            //   CANCELLED → CANCELLED · DELIVERED → DELIVERED · READY → READY
            //   PENDING / CONFIRMED / IN_PREPARATION → IN_PREPARATION
            //
            // Los pendientes y confirmados NO se dejan en HELD: HELD significa "esperando a que
            // un PDA lo marche", y ningún cliente v1 va a mandar ese marchado — se quedarían
            // colgados para siempre y, peor, su comanda nunca se auto-finalizaría (RF-1814).
            // IN_PREPARATION los deja donde el servicio los tenía: camino de la estación.
            migrationBuilder.Sql("""
                UPDATE order_items AS oi
                SET tenant_id    = o.tenant_id,
                    station_id   = o.station_id,
                    updated_at   = o.updated_at,
                    timing       = 'NOW',
                    status       = CASE o.status
                                       WHEN 'CANCELLED' THEN 'CANCELLED'
                                       WHEN 'DELIVERED' THEN 'DELIVERED'
                                       WHEN 'READY'     THEN 'READY'
                                       ELSE 'IN_PREPARATION'
                                   END,
                    ready_at     = CASE WHEN o.status IN ('READY', 'DELIVERED')
                                        THEN o.ready_at END,
                    -- orders no tiene delivered_at; su último cambio de estado es lo más cerca
                    -- que hay del momento de la entrega.
                    delivered_at = CASE WHEN o.status = 'DELIVERED' THEN o.updated_at END
                FROM orders AS o
                WHERE o.id = oi.order_id;
                """);

            migrationBuilder.CreateTable(
                name: "delivery_trips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_trips", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_delivery_trip_id",
                table: "order_items",
                column: "delivery_trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_tenant_id_station_id_status",
                table: "order_items",
                columns: new[] { "tenant_id", "station_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_tenant_id_status",
                table: "order_items",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_tenant_id_updated_at",
                table: "order_items",
                columns: new[] { "tenant_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_delivery_trips_runner_open",
                table: "delivery_trips",
                column: "runner_id",
                unique: true,
                filter: "\"status\" = 'OPEN'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_trips");

            migrationBuilder.DropIndex(
                name: "IX_order_items_delivery_trip_id",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "IX_order_items_tenant_id_station_id_status",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "IX_order_items_tenant_id_status",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "IX_order_items_tenant_id_updated_at",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "delivery_trip_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "marched_at",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "marched_by",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "ready_at",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "station_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "status",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "timing",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "order_items");
        }
    }
}
