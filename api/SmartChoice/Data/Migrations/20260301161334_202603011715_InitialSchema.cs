using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace SmartChoice.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202603011715_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    username = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    max_uses = table.Column<int>(type: "int", nullable: false),
                    used_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invites", x => x.id);
                    table.CheckConstraint("ck_invites_max_uses", "`max_uses` > 0");
                    table.CheckConstraint("ck_invites_used_count", "`used_count` >= 0 AND `used_count` <= `max_uses`");
                    table.ForeignKey(
                        name: "FK_invites_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateTable(
                name: "polls",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    question = table.Column<string>(type: "varchar(280)", maxLength: 280, nullable: false),
                    status = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)1),
                    starts_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ends_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_polls", x => x.id);
                    table.CheckConstraint("ck_polls_dates", "`ends_at` IS NULL OR `starts_at` IS NULL OR `ends_at` > `starts_at`");
                    table.ForeignKey(
                        name: "FK_polls_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateTable(
                name: "guest_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    invite_id = table.Column<long>(type: "bigint", nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    is_revoked = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guest_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_guest_tokens_invites_invite_id",
                        column: x => x.invite_id,
                        principalTable: "invites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateTable(
                name: "poll_photos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    poll_id = table.Column<long>(type: "bigint", nullable: false),
                    photo_url = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    display_order = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poll_photos", x => x.id);
                    table.CheckConstraint("ck_poll_photos_display_order", "`display_order` >= 1 AND `display_order` <= 4");
                    table.ForeignKey(
                        name: "FK_poll_photos_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    poll_id = table.Column<long>(type: "bigint", nullable: false),
                    reporter_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reporter_guest_token_id = table.Column<long>(type: "bigint", nullable: true),
                    reason_code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    details = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)0),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.id);
                    table.CheckConstraint("ck_reports_actor", "((`reporter_user_id` IS NOT NULL AND `reporter_guest_token_id` IS NULL) OR (`reporter_user_id` IS NULL AND `reporter_guest_token_id` IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_reports_guest_tokens_reporter_guest_token_id",
                        column: x => x.reporter_guest_token_id,
                        principalTable: "guest_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reports_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateTable(
                name: "votes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    poll_id = table.Column<long>(type: "bigint", nullable: false),
                    poll_photo_id = table.Column<long>(type: "bigint", nullable: false),
                    voter_user_id = table.Column<long>(type: "bigint", nullable: true),
                    guest_token_id = table.Column<long>(type: "bigint", nullable: true),
                    voted_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_votes", x => x.id);
                    table.CheckConstraint("ck_votes_actor", "((`voter_user_id` IS NOT NULL AND `guest_token_id` IS NULL) OR (`voter_user_id` IS NULL AND `guest_token_id` IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_votes_guest_tokens_guest_token_id",
                        column: x => x.guest_token_id,
                        principalTable: "guest_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_votes_poll_photos_poll_photo_id",
                        column: x => x.poll_photo_id,
                        principalTable: "poll_photos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_votes_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_votes_users_voter_user_id",
                        column: x => x.voter_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_0900_ai_ci");

            migrationBuilder.CreateIndex(
                name: "ix_guest_tokens_invite_created_at",
                table: "guest_tokens",
                columns: new[] { "invite_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_guest_tokens_revoked_expires_at",
                table: "guest_tokens",
                columns: new[] { "is_revoked", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_guest_tokens_token_hash",
                table: "guest_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invites_active_expires_at",
                table: "invites",
                columns: new[] { "is_active", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_invites_created_by_user_id",
                table: "invites",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_invites_code",
                table: "invites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_poll_photos_poll_id",
                table: "poll_photos",
                column: "poll_id");

            migrationBuilder.CreateIndex(
                name: "ux_poll_photos_poll_order",
                table: "poll_photos",
                columns: new[] { "poll_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_polls_author_created_at",
                table: "polls",
                columns: new[] { "author_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_polls_ends_at_status",
                table: "polls",
                columns: new[] { "ends_at", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_polls_status_created_at",
                table: "polls",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_poll_created_at",
                table: "reports",
                columns: new[] { "poll_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_guest_token_id",
                table: "reports",
                column: "reporter_guest_token_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_user_id",
                table: "reports",
                column: "reporter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_status_created_at",
                table: "reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_is_active_created_at",
                table: "users",
                columns: new[] { "is_active", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_votes_guest_token_id",
                table: "votes",
                column: "guest_token_id");

            migrationBuilder.CreateIndex(
                name: "ix_votes_poll_photo",
                table: "votes",
                columns: new[] { "poll_id", "poll_photo_id" });

            migrationBuilder.CreateIndex(
                name: "IX_votes_poll_photo_id",
                table: "votes",
                column: "poll_photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_votes_poll_voted_at",
                table: "votes",
                columns: new[] { "poll_id", "voted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_votes_voter_user_id",
                table: "votes",
                column: "voter_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_votes_poll_guest_token",
                table: "votes",
                columns: new[] { "poll_id", "guest_token_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_votes_poll_user",
                table: "votes",
                columns: new[] { "poll_id", "voter_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "votes");

            migrationBuilder.DropTable(
                name: "guest_tokens");

            migrationBuilder.DropTable(
                name: "poll_photos");

            migrationBuilder.DropTable(
                name: "invites");

            migrationBuilder.DropTable(
                name: "polls");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
