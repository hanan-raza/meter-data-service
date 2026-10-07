using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeterDataService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "anchor_value_after",
                table: "measurement_values",
                type: "numeric(18,5)",
                precision: 18,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "anchor_value_before",
                table: "measurement_values",
                type: "numeric(18,5)",
                precision: 18,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "replaced_by",
                table: "measurement_values",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anchor_value_after",
                table: "measurement_values");

            migrationBuilder.DropColumn(
                name: "anchor_value_before",
                table: "measurement_values");

            migrationBuilder.DropColumn(
                name: "replaced_by",
                table: "measurement_values");
        }
    }
}
