using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartChoice.Data;

#nullable disable

namespace SmartChoice.Data.Migrations
{
    [DbContext(typeof(SmartChoiceDbContext))]
    [Migration("20260529090000_ScopeGuestTokensToPoll")]
    public partial class ScopeGuestTokensToPoll : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "poll_id",
                table: "guest_tokens",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE guest_tokens gt
                JOIN votes v ON v.guest_token_id = gt.id
                SET gt.poll_id = v.poll_id
                WHERE gt.poll_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE guest_tokens gt
                JOIN reports r ON r.reporter_guest_token_id = gt.id
                SET gt.poll_id = r.poll_id
                WHERE gt.poll_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                DELETE gt
                FROM guest_tokens gt
                WHERE gt.poll_id IS NULL;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "poll_id",
                table: "guest_tokens",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_guest_tokens_poll_id",
                table: "guest_tokens",
                column: "poll_id");

            migrationBuilder.CreateIndex(
                name: "ix_guest_tokens_poll_revoked_expires_at",
                table: "guest_tokens",
                columns: new[] { "poll_id", "is_revoked", "expires_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_guest_tokens_polls_poll_id",
                table: "guest_tokens",
                column: "poll_id",
                principalTable: "polls",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_guest_tokens_polls_poll_id",
                table: "guest_tokens");

            migrationBuilder.DropIndex(
                name: "ix_guest_tokens_poll_id",
                table: "guest_tokens");

            migrationBuilder.DropIndex(
                name: "ix_guest_tokens_poll_revoked_expires_at",
                table: "guest_tokens");

            migrationBuilder.DropColumn(
                name: "poll_id",
                table: "guest_tokens");
        }
    }
}
