using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeterDataService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementSourceDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "source_day",
                table: "measurement_values",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_day",
                table: "measurement_values");
        }
    }
}
