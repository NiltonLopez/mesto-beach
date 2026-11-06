using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantVenueModeAndChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "venue_mode",
                table: "tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "RESTAURANT");

            migrationBuilder.AddColumn<DateTime>(
                name: "venue_mode_changed_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "venue_mode_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    to_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    preflight = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue_mode_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_venue_mode_changes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_venue_mode_changes_tenant_changed_at",
                table: "venue_mode_changes",
                columns: new[] { "tenant_id", "changed_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "venue_mode_changes");

            migrationBuilder.DropColumn(
                name: "venue_mode",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "venue_mode_changed_at",
                table: "tenants");
        }
    }
}
