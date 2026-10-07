using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionThinkingLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "thinking_level",
                schema: "vibemaker",
                table: "agent_executions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "thinking_level",
                schema: "vibemaker",
                table: "agent_executions");
        }
    }
}
