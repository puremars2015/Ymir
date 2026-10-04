using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialVibeMaker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vibemaker");

            migrationBuilder.CreateTable(
                name: "workspaces",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_runtimes",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    provider_runtime_id = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    image_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_runtimes", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_runtimes_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalSchema: "vibemaker",
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalSchema: "vibemaker",
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    runtime_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    provider_session_id = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_sessions_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "vibemaker",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    message_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    sequence_no = table.Column<long>(type: "bigint", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "vibemaker",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_executions",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    runtime_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    assistant_message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_executions_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "vibemaker",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_executions_messages_user_message_id",
                        column: x => x.user_message_id,
                        principalSchema: "vibemaker",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_events",
                schema: "vibemaker",
                columns: table => new
                {
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_events", x => new { x.execution_id, x.sequence });
                    table.ForeignKey(
                        name: "fk_execution_events_agent_executions_execution_id",
                        column: x => x.execution_id,
                        principalSchema: "vibemaker",
                        principalTable: "agent_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_executions_status",
                schema: "vibemaker",
                table: "agent_executions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_agent_executions_user_id_client_request_id",
                schema: "vibemaker",
                table: "agent_executions",
                columns: new[] { "user_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_executions_user_message_id",
                schema: "vibemaker",
                table: "agent_executions",
                column: "user_message_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_executions_active_per_conversation",
                schema: "vibemaker",
                table: "agent_executions",
                column: "conversation_id",
                unique: true,
                filter: "[status] IN ('QUEUED', 'RUNNING')");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runtimes_workspace_id",
                schema: "vibemaker",
                table: "agent_runtimes",
                column: "workspace_id",
                unique: true,
                filter: "[status] <> 'DELETED'");

            migrationBuilder.CreateIndex(
                name: "ix_agent_sessions_conversation_id",
                schema: "vibemaker",
                table: "agent_sessions",
                column: "conversation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_id_workspace_id",
                schema: "vibemaker",
                table: "conversations",
                columns: new[] { "user_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_workspace_id",
                schema: "vibemaker",
                table: "conversations",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_sequence_no",
                schema: "vibemaker",
                table: "messages",
                columns: new[] { "conversation_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_user_id",
                schema: "vibemaker",
                table: "workspaces",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_runtimes",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "agent_sessions",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "execution_events",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "agent_executions",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "workspaces",
                schema: "vibemaker");
        }
    }
}
