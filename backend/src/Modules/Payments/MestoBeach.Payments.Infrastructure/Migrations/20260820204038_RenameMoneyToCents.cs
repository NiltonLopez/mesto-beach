using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameMoneyToCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "opening_float_cop",
                table: "shifts",
                newName: "opening_float_cents");

            migrationBuilder.RenameColumn(
                name: "total_cop",
                table: "payments",
                newName: "total_cents");

            migrationBuilder.RenameColumn(
                name: "tip_cop",
                table: "payments",
                newName: "tip_cents");

            migrationBuilder.RenameColumn(
                name: "commission_cop",
                table: "payments",
                newName: "commission_cents");

            migrationBuilder.RenameColumn(
                name: "amount_cop",
                table: "payments",
                newName: "amount_cents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "opening_float_cents",
                table: "shifts",
                newName: "opening_float_cop");

            migrationBuilder.RenameColumn(
                name: "total_cents",
                table: "payments",
                newName: "total_cop");

            migrationBuilder.RenameColumn(
                name: "tip_cents",
                table: "payments",
                newName: "tip_cop");

            migrationBuilder.RenameColumn(
                name: "commission_cents",
                table: "payments",
                newName: "commission_cop");

            migrationBuilder.RenameColumn(
                name: "amount_cents",
                table: "payments",
                newName: "amount_cop");
        }
    }
}
