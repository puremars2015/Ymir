using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneRuntimePerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0007：一個使用者一個 runtime；Workspace 改為 Project（檔案群組）。
            // 以 rename 保留既有資料：原本的 workspace 變成專案，原本的對話留在該專案中。
            migrationBuilder.DropForeignKey(
                name: "fk_agent_runtimes_workspaces_workspace_id",
                schema: "vibemaker",
                table: "agent_runtimes");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_workspaces_workspace_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_user_id_workspace_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_workspace_id",
                schema: "vibemaker",
                table: "conversations");

            // workspaces → projects
            migrationBuilder.RenameTable(
                name: "workspaces",
                schema: "vibemaker",
                newName: "projects",
                newSchema: "vibemaker");

            migrationBuilder.DropColumn(
                name: "storage_key",
                schema: "vibemaker",
                table: "projects");

            migrationBuilder.Sql("EXEC sp_rename N'vibemaker.projects.pk_workspaces', N'pk_projects', N'INDEX';");

            migrationBuilder.RenameIndex(
                name: "ix_workspaces_user_id",
                schema: "vibemaker",
                table: "projects",
                newName: "ix_projects_user_id");

            // conversations.workspace_id → project_id（可為 null：未分組的對話）
            migrationBuilder.RenameColumn(
                name: "workspace_id",
                schema: "vibemaker",
                table: "conversations",
                newName: "project_id");

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                schema: "vibemaker",
                table: "conversations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            // 舊的 runtime 紀錄是「每個 workspace 一個」，無法對應到使用者；清空後於下次執行時以使用者重新建立。
            // 舊的 ymir-ws-* container 需手動移除（docs/guides/windows-docker.md）。
            migrationBuilder.Sql("DELETE FROM [vibemaker].[agent_runtimes];");

            migrationBuilder.RenameColumn(
                name: "workspace_id",
                schema: "vibemaker",
                table: "agent_runtimes",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_agent_runtimes_workspace_id",
                schema: "vibemaker",
                table: "agent_runtimes",
                newName: "ix_agent_runtimes_user_id");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                schema: "vibemaker",
                table: "agent_sessions");

            migrationBuilder.DropColumn(
                name: "workspace_id",
                schema: "vibemaker",
                table: "agent_executions");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_project_id",
                schema: "vibemaker",
                table: "conversations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_user_id_project_id",
                schema: "vibemaker",
                table: "conversations",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_projects_project_id",
                schema: "vibemaker",
                table: "conversations",
                column: "project_id",
                principalSchema: "vibemaker",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        /// <remarks>開發用的結構還原：workspaces 會重建為空表，專案與對話的對應不會還原。</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversations_projects_project_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "vibemaker");

            migrationBuilder.DropIndex(
                name: "ix_conversations_project_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_user_id_project_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "project_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "vibemaker",
                table: "agent_runtimes",
                newName: "workspace_id");

            migrationBuilder.RenameIndex(
                name: "ix_agent_runtimes_user_id",
                schema: "vibemaker",
                table: "agent_runtimes",
                newName: "ix_agent_runtimes_workspace_id");

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                schema: "vibemaker",
                table: "conversations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                schema: "vibemaker",
                table: "agent_sessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "workspace_id",
                schema: "vibemaker",
                table: "agent_executions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "workspaces",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                });

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
                name: "ix_workspaces_user_id",
                schema: "vibemaker",
                table: "workspaces",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_runtimes_workspaces_workspace_id",
                schema: "vibemaker",
                table: "agent_runtimes",
                column: "workspace_id",
                principalSchema: "vibemaker",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_workspaces_workspace_id",
                schema: "vibemaker",
                table: "conversations",
                column: "workspace_id",
                principalSchema: "vibemaker",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
