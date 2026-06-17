using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeClientReleaseRetentionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE edge_client_host_releases
                SET status = 'Archived'
                WHERE status = 'Revoked';
                """);

            migrationBuilder.Sql("""
                UPDATE edge_client_plugin_releases
                SET status = 'Archived'
                WHERE status = 'Revoked';
                """);

            migrationBuilder.CreateTable(
                name: "edge_client_release_retention_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_versions_per_component = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_release_retention_policies", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_client_release_retention_policies");

            migrationBuilder.Sql("""
                UPDATE edge_client_host_releases
                SET status = 'Revoked'
                WHERE status = 'Archived';
                """);

            migrationBuilder.Sql("""
                UPDATE edge_client_plugin_releases
                SET status = 'Revoked'
                WHERE status = 'Archived';
                """);
        }
    }
}
