using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    chunk_count = table.Column<int>(type: "int", nullable: false),
                    embedding_model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    dimensions = table.Column<int>(type: "int", nullable: true),
                    error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_project_id_file_name",
                schema: "vibemaker",
                table: "knowledge_documents",
                columns: new[] { "project_id", "file_name" },
                unique: true,
                filter: "[status] = 'READY'");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_project_id_status",
                schema: "vibemaker",
                table: "knowledge_documents",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_status",
                schema: "vibemaker",
                table: "knowledge_documents",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_documents",
                schema: "vibemaker");
        }
    }
}
