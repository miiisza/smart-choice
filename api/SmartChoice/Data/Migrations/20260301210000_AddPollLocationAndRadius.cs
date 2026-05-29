using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SmartChoice.Data;

#nullable disable

namespace SmartChoice.Data.Migrations
{
    [DbContext(typeof(SmartChoiceDbContext))]
    [Migration("20260301210000_AddPollLocationAndRadius")]
    public partial class AddPollLocationAndRadius : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "polls",
                type: "double",
                nullable: false,
                defaultValue: 0d);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "polls",
                type: "double",
                nullable: false,
                defaultValue: 0d);

            migrationBuilder.AddColumn<int>(
                name: "radius_meters",
                table: "polls",
                type: "int",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddCheckConstraint(
                name: "ck_polls_latitude",
                table: "polls",
                sql: "`latitude` >= -90 AND `latitude` <= 90");

            migrationBuilder.AddCheckConstraint(
                name: "ck_polls_longitude",
                table: "polls",
                sql: "`longitude` >= -180 AND `longitude` <= 180");

            migrationBuilder.AddCheckConstraint(
                name: "ck_polls_radius_meters",
                table: "polls",
                sql: "`radius_meters` >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_polls_lat_lng",
                table: "polls",
                columns: new[] { "latitude", "longitude" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_polls_latitude",
                table: "polls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_polls_longitude",
                table: "polls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_polls_radius_meters",
                table: "polls");

            migrationBuilder.DropIndex(
                name: "ix_polls_lat_lng",
                table: "polls");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "polls");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "polls");

            migrationBuilder.DropColumn(
                name: "radius_meters",
                table: "polls");
        }
    }
}
