using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartChoice.Data;

#nullable disable

namespace SmartChoice.Data.Migrations
{
    [DbContext(typeof(SmartChoiceDbContext))]
    [Migration("20260302001000_FixPollStatusDefaultDraft")]
    public partial class FixPollStatusDefaultDraft : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "status",
                table: "polls",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0,
                oldClrType: typeof(byte),
                oldType: "tinyint unsigned",
                oldDefaultValue: (byte)1);

            migrationBuilder.Sql(
                """
                UPDATE polls p
                LEFT JOIN (
                    SELECT poll_id, COUNT(*) AS photo_count
                    FROM poll_photos
                    GROUP BY poll_id
                ) pp ON pp.poll_id = p.id
                SET p.status = 0
                WHERE p.status = 1
                  AND COALESCE(pp.photo_count, 0) < 2;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "status",
                table: "polls",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)1,
                oldClrType: typeof(byte),
                oldType: "tinyint unsigned",
                oldDefaultValue: (byte)0);
        }
    }
}
