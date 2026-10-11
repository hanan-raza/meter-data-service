using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeterDataService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "series_annotations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    measurement_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    score = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series_annotations", x => x.id);
                    table.ForeignKey(
                        name: "fk_series_annotations_measurement_series_measurement_series_id",
                        column: x => x.measurement_series_id,
                        principalTable: "measurement_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_series_annotations_measurement_series_id_interval_start",
                table: "series_annotations",
                columns: new[] { "measurement_series_id", "interval_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "series_annotations");
        }
    }
}
