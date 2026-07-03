using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    host_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remark = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_hosts", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_hosts_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edge_host_plc_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edge_host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plc_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    plc_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    station_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    protocol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    remark = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_host_plc_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_host_plc_bindings_devices_business_device_id",
                        column: x => x.business_device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_edge_host_plc_bindings_edge_hosts_edge_host_id",
                        column: x => x.edge_host_id,
                        principalTable: "edge_hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_edge_host_plc_bindings_mfg_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "mfg_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_edge_host_plc_bindings_business_device_id",
                table: "edge_host_plc_bindings",
                column: "business_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_edge_host_plc_bindings_process_id",
                table: "edge_host_plc_bindings",
                column: "process_id");

            migrationBuilder.CreateIndex(
                name: "ux_edge_host_plc_bindings_host_plc",
                table: "edge_host_plc_bindings",
                columns: new[] { "edge_host_id", "plc_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_edge_hosts_client_code",
                table: "edge_hosts",
                column: "client_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_edge_hosts_device_id",
                table: "edge_hosts",
                column: "device_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_host_plc_bindings");

            migrationBuilder.DropTable(
                name: "edge_hosts");
        }
    }
}
