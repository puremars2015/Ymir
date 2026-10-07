using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execution_artifacts",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_execution_artifacts_agent_executions_execution_id",
                        column: x => x.execution_id,
                        principalSchema: "vibemaker",
                        principalTable: "agent_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_execution_artifacts_execution_id",
                schema: "vibemaker",
                table: "execution_artifacts",
                column: "execution_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "execution_artifacts",
                schema: "vibemaker");
        }
    }
}
