using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddClientReleaseComponentDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_client_release_component_deletions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    component_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    versions_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_user_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_release_component_deletions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "edge_client_release_component_deletion_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deletion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relative_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    artifact_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_release_component_deletion_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_client_release_component_deletion_files_edge_client_release_component_deletions_deletion_id",
                        column: x => x.deletion_id,
                        principalTable: "edge_client_release_component_deletions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_release_component_deletions_status",
                table: "edge_client_release_component_deletions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_release_component_deletions_component",
                table: "edge_client_release_component_deletions",
                column: "component_id");

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_release_component_deletion_files_deletion",
                table: "edge_client_release_component_deletion_files",
                column: "deletion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_client_release_component_deletion_files");

            migrationBuilder.DropTable(
                name: "edge_client_release_component_deletions");
        }
    }
}
