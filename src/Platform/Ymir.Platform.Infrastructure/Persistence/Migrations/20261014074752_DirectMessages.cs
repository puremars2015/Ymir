using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "direct_messages",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sender_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_direct_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_direct_messages_users_recipient_id",
                        column: x => x.recipient_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_direct_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_direct_messages_recipient_id_read_at",
                schema: "platform",
                table: "direct_messages",
                columns: new[] { "recipient_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_direct_messages_recipient_id_sender_id_created_at",
                schema: "platform",
                table: "direct_messages",
                columns: new[] { "recipient_id", "sender_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_direct_messages_sender_id_recipient_id_created_at",
                schema: "platform",
                table: "direct_messages",
                columns: new[] { "sender_id", "recipient_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "direct_messages",
                schema: "platform");
        }
    }
}
