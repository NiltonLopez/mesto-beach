using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    amount_cop = table.Column<int>(type: "integer", nullable: false),
                    tip_cop = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_cop = table.Column<int>(type: "integer", nullable: false),
                    commission_cop = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    gateway_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gateway_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    split_index = table.Column<int>(type: "integer", nullable: true),
                    split_total = table.Column<int>(type: "integer", nullable: true),
                    processed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_payment_configs",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    digital_payments_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    gateway_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    gateway_public_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    gateway_private_key_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tip_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    tip_options = table.Column<string>(type: "jsonb", nullable: false),
                    commission_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 3.5m),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_payment_configs", x => x.tenant_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_session_id",
                table: "payments",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_session_id_split_index",
                table: "payments",
                columns: new[] { "session_id", "split_index" },
                unique: true,
                filter: "split_index IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payments_tenant_id_created_at",
                table: "payments",
                columns: new[] { "tenant_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "tenant_payment_configs");
        }
    }
}
