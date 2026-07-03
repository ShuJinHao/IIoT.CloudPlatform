using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeHostPlcRuntimeStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_host_plc_runtime_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edge_host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    plc_binding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plc_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reported_plc_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false),
                    runtime_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    station_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    protocol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_host_plc_runtime_states", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_host_plc_runtime_states_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_edge_host_plc_runtime_states_edge_host_plc_bindings_plc_bin~",
                        column: x => x.plc_binding_id,
                        principalTable: "edge_host_plc_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_edge_host_plc_runtime_states_edge_hosts_edge_host_id",
                        column: x => x.edge_host_id,
                        principalTable: "edge_hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_edge_host_plc_runtime_states_edge_host_id",
                table: "edge_host_plc_runtime_states",
                column: "edge_host_id");

            migrationBuilder.CreateIndex(
                name: "ix_edge_host_plc_runtime_states_last_seen",
                table: "edge_host_plc_runtime_states",
                column: "last_seen_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_edge_host_plc_runtime_states_plc_binding_id",
                table: "edge_host_plc_runtime_states",
                column: "plc_binding_id");

            migrationBuilder.CreateIndex(
                name: "ux_edge_host_plc_runtime_states_device_client_plc",
                table: "edge_host_plc_runtime_states",
                columns: new[] { "device_id", "client_code", "plc_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_host_plc_runtime_states");
        }
    }
}
