using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_groups",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    owner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_groups_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "chat_group_members",
                schema: "platform",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_group_members", x => new { x.group_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_chat_group_members_chat_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "platform",
                        principalTable: "chat_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_group_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "chat_group_messages",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sender_kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    sender_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 16001, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    invoked_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bot_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_group_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_group_messages_chat_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "platform",
                        principalTable: "chat_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_group_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_group_members_user_id",
                schema: "platform",
                table: "chat_group_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_group_messages_bot_status",
                schema: "platform",
                table: "chat_group_messages",
                column: "bot_status",
                filter: "[bot_status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "ix_chat_group_messages_execution_id",
                schema: "platform",
                table: "chat_group_messages",
                column: "execution_id",
                unique: true,
                filter: "[execution_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_group_messages_group_id_created_at",
                schema: "platform",
                table: "chat_group_messages",
                columns: new[] { "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_group_messages_sender_id",
                schema: "platform",
                table: "chat_group_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_groups_owner_id",
                schema: "platform",
                table: "chat_groups",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_group_members",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "chat_group_messages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "chat_groups",
                schema: "platform");
        }
    }
}
