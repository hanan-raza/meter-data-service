using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeterDataService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    malo_id = table.Column<string>(type: "character(11)", fixedLength: true, maxLength: 11, nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meter_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    melo_id = table.Column<string>(type: "character(33)", fixedLength: true, maxLength: 33, nullable: false),
                    market_location_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_locations", x => x.id);
                    table.ForeignKey(
                        name: "fk_meter_locations_market_locations_market_location_id",
                        column: x => x.market_location_id,
                        principalTable: "market_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "measurement_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obis_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_series", x => x.id);
                    table.ForeignKey(
                        name: "fk_measurement_series_meter_locations_meter_location_id",
                        column: x => x.meter_location_id,
                        principalTable: "meter_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    meter_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meters", x => x.id);
                    table.ForeignKey(
                        name: "fk_meters_meter_locations_meter_location_id",
                        column: x => x.meter_location_id,
                        principalTable: "meter_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "measurement_values",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    measurement_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_measurement_values_measurement_series_measurement_series_id",
                        column: x => x.measurement_series_id,
                        principalTable: "measurement_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_locations_malo_id",
                table: "market_locations",
                column: "malo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_measurement_series_meter_location_id_obis_code",
                table: "measurement_series",
                columns: new[] { "meter_location_id", "obis_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_measurement_values_measurement_series_id_interval_start",
                table: "measurement_values",
                columns: new[] { "measurement_series_id", "interval_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meter_locations_market_location_id",
                table: "meter_locations",
                column: "market_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_meter_locations_melo_id",
                table: "meter_locations",
                column: "melo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meters_meter_location_id_active",
                table: "meters",
                column: "meter_location_id",
                unique: true,
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meters_serial_number",
                table: "meters",
                column: "serial_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "measurement_values");

            migrationBuilder.DropTable(
                name: "meters");

            migrationBuilder.DropTable(
                name: "measurement_series");

            migrationBuilder.DropTable(
                name: "meter_locations");

            migrationBuilder.DropTable(
                name: "market_locations");
        }
    }
}
