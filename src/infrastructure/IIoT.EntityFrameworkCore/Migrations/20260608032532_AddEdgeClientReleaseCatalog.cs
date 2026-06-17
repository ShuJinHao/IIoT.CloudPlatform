using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeClientReleaseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_client_host_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_framework = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    download_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    package_size = table.Column<long>(type: "bigint", nullable: false),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    signature = table.Column<string>(type: "text", nullable: true),
                    publisher = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_host_releases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "edge_client_plugin_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    icon_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    accent_color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    min_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    max_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_framework = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    download_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    package_size = table.Column<long>(type: "bigint", nullable: false),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    dependencies_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    signature = table.Column<string>(type: "text", nullable: true),
                    publisher = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_plugin_releases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "edge_device_client_version_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_device_client_version_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_device_client_version_snapshots_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edge_device_client_plugin_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_client_version_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_device_client_plugin_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_device_client_plugin_versions_edge_device_client_versi~",
                        column: x => x.device_client_version_snapshot_id,
                        principalTable: "edge_device_client_version_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_host_releases_catalog",
                table: "edge_client_host_releases",
                columns: new[] { "channel", "target_runtime", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_edge_client_host_releases_identity",
                table: "edge_client_host_releases",
                columns: new[] { "channel", "version", "target_runtime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_plugin_releases_catalog",
                table: "edge_client_plugin_releases",
                columns: new[] { "channel", "target_runtime", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_edge_client_plugin_releases_identity",
                table: "edge_client_plugin_releases",
                columns: new[] { "module_id", "channel", "version", "target_runtime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_edge_device_client_plugin_versions_module",
                table: "edge_device_client_plugin_versions",
                columns: new[] { "device_client_version_snapshot_id", "module_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_edge_device_client_version_snapshots_device",
                table: "edge_device_client_version_snapshots",
                column: "device_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_client_host_releases");

            migrationBuilder.DropTable(
                name: "edge_client_plugin_releases");

            migrationBuilder.DropTable(
                name: "edge_device_client_plugin_versions");

            migrationBuilder.DropTable(
                name: "edge_device_client_version_snapshots");
        }
    }
}
