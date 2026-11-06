using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── tenants: platform admin columns ──────────────────────────────

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_email",
                table: "tenants",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_notes",
                table: "tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "commission_rate",
                table: "tenants",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subscription_amount_cop",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "trial_ends_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            // ── platform_users: audit columns ────────────────────────────────

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "platform_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deactivated_at",
                table: "platform_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deactivated_by",
                table: "platform_users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "city",                    table: "tenants");
            migrationBuilder.DropColumn(name: "owner_email",             table: "tenants");
            migrationBuilder.DropColumn(name: "internal_notes",          table: "tenants");
            migrationBuilder.DropColumn(name: "commission_rate",         table: "tenants");
            migrationBuilder.DropColumn(name: "subscription_amount_cop", table: "tenants");
            migrationBuilder.DropColumn(name: "trial_ends_at",           table: "tenants");
            migrationBuilder.DropColumn(name: "created_by",              table: "tenants");
            migrationBuilder.DropColumn(name: "updated_at",              table: "tenants");

            migrationBuilder.DropColumn(name: "created_by",      table: "platform_users");
            migrationBuilder.DropColumn(name: "deactivated_at",  table: "platform_users");
            migrationBuilder.DropColumn(name: "deactivated_by",  table: "platform_users");
        }
    }
}
