using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SmartChoice.Data;

#nullable disable

namespace SmartChoice.Data.Migrations
{
    [DbContext(typeof(SmartChoiceDbContext))]
    [Migration("20260301190000_AddPollPhotoStorageMetadata")]
    public partial class AddPollPhotoStorageMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "poll_photos",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "file_size_bytes",
                table: "poll_photos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "poll_photos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_key",
                table: "poll_photos",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_storage_key",
                table: "poll_photos",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "thumbnail_height",
                table: "poll_photos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_url",
                table: "poll_photos",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "thumbnail_width",
                table: "poll_photos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "poll_photos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_poll_photos_storage_key",
                table: "poll_photos",
                column: "storage_key",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_poll_photos_storage_key",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "file_size_bytes",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "height",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "storage_key",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "thumbnail_storage_key",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "thumbnail_height",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "thumbnail_url",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "thumbnail_width",
                table: "poll_photos");

            migrationBuilder.DropColumn(
                name: "width",
                table: "poll_photos");
        }
    }
}
