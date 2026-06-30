using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeDeviceRuntimeHeartbeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_device_runtime_heartbeats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    runtime_instance_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    machine_profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    local_ip_addresses_json = table.Column<string>(type: "jsonb", nullable: false),
                    remote_ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_stopped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_device_runtime_heartbeats", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_device_runtime_heartbeats_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_edge_device_runtime_heartbeats_last_heartbeat",
                table: "edge_device_runtime_heartbeats",
                column: "last_heartbeat_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_edge_device_runtime_heartbeats_device_client",
                table: "edge_device_runtime_heartbeats",
                columns: new[] { "device_id", "client_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_device_runtime_heartbeats");
        }
    }
}
