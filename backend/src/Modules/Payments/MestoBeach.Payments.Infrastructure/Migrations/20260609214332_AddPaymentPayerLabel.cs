using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MestoBeach.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentPayerLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payer_label",
                table: "payments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payer_label",
                table: "payments");
        }
    }
}
