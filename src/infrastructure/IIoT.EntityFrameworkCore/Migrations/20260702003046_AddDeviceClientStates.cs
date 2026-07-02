using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceClientStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_device_client_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    version_local_ip_addresses_json = table.Column<string>(type: "jsonb", nullable: false),
                    version_remote_ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    version_reported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version_received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    runtime_instance_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    machine_profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    runtime_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    runtime_host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    runtime_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    runtime_local_ip_addresses_json = table.Column<string>(type: "jsonb", nullable: false),
                    runtime_remote_ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    runtime_started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_runtime_heartbeat_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_runtime_stopped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_device_client_states", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_device_client_states_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                with snapshot_states as (
                    select
                        snapshot.device_id,
                        upper(trim(snapshot.client_code)) as client_code,
                        snapshot.channel,
                        snapshot.host_version,
                        snapshot.host_api_version,
                        coalesce(snapshot.local_ip_addresses_json, '[]'::jsonb) as version_local_ip_addresses_json,
                        snapshot.remote_ip_address as version_remote_ip_address,
                        snapshot.reported_at_utc as version_reported_at_utc,
                        snapshot.received_at_utc as version_received_at_utc,
                        heartbeat.runtime_instance_id,
                        heartbeat.machine_profile,
                        heartbeat.host_version as runtime_host_version,
                        heartbeat.host_api_version as runtime_host_api_version,
                        heartbeat.status as runtime_status,
                        coalesce(heartbeat.local_ip_addresses_json, '[]'::jsonb) as runtime_local_ip_addresses_json,
                        heartbeat.remote_ip_address as runtime_remote_ip_address,
                        heartbeat.started_at_utc as runtime_started_at_utc,
                        heartbeat.last_heartbeat_at_utc as last_runtime_heartbeat_at_utc,
                        heartbeat.last_stopped_at_utc as last_runtime_stopped_at_utc,
                        md5(snapshot.device_id::text || ':' || upper(trim(snapshot.client_code))) as hash,
                        coalesce(least(snapshot.received_at_utc, heartbeat.created_at_utc), snapshot.received_at_utc, heartbeat.created_at_utc, now()) as created_at_utc,
                        coalesce(greatest(snapshot.received_at_utc, heartbeat.updated_at_utc), snapshot.received_at_utc, heartbeat.updated_at_utc, now()) as updated_at_utc
                    from edge_device_client_version_snapshots snapshot
                    left join edge_device_runtime_heartbeats heartbeat
                        on heartbeat.device_id = snapshot.device_id
                       and upper(trim(heartbeat.client_code)) = upper(trim(snapshot.client_code))
                )
                insert into edge_device_client_states (
                    id,
                    device_id,
                    client_code,
                    channel,
                    host_version,
                    host_api_version,
                    version_local_ip_addresses_json,
                    version_remote_ip_address,
                    version_reported_at_utc,
                    version_received_at_utc,
                    runtime_instance_id,
                    machine_profile,
                    runtime_host_version,
                    runtime_host_api_version,
                    runtime_status,
                    runtime_local_ip_addresses_json,
                    runtime_remote_ip_address,
                    runtime_started_at_utc,
                    last_runtime_heartbeat_at_utc,
                    last_runtime_stopped_at_utc,
                    created_at_utc,
                    updated_at_utc)
                select
                    (substr(hash, 1, 8) || '-' || substr(hash, 9, 4) || '-' || substr(hash, 13, 4) || '-' || substr(hash, 17, 4) || '-' || substr(hash, 21, 12))::uuid,
                    device_id,
                    client_code,
                    channel,
                    host_version,
                    host_api_version,
                    version_local_ip_addresses_json,
                    version_remote_ip_address,
                    version_reported_at_utc,
                    version_received_at_utc,
                    runtime_instance_id,
                    machine_profile,
                    runtime_host_version,
                    runtime_host_api_version,
                    runtime_status,
                    runtime_local_ip_addresses_json,
                    runtime_remote_ip_address,
                    runtime_started_at_utc,
                    last_runtime_heartbeat_at_utc,
                    last_runtime_stopped_at_utc,
                    created_at_utc,
                    updated_at_utc
                from snapshot_states;
                """);

            migrationBuilder.Sql(
                """
                with heartbeat_states as (
                    select
                        heartbeat.device_id,
                        upper(trim(heartbeat.client_code)) as client_code,
                        heartbeat.runtime_instance_id,
                        heartbeat.machine_profile,
                        heartbeat.host_version as runtime_host_version,
                        heartbeat.host_api_version as runtime_host_api_version,
                        heartbeat.status as runtime_status,
                        coalesce(heartbeat.local_ip_addresses_json, '[]'::jsonb) as runtime_local_ip_addresses_json,
                        heartbeat.remote_ip_address as runtime_remote_ip_address,
                        heartbeat.started_at_utc as runtime_started_at_utc,
                        heartbeat.last_heartbeat_at_utc as last_runtime_heartbeat_at_utc,
                        heartbeat.last_stopped_at_utc as last_runtime_stopped_at_utc,
                        md5(heartbeat.device_id::text || ':' || upper(trim(heartbeat.client_code))) as hash,
                        coalesce(heartbeat.created_at_utc, now()) as created_at_utc,
                        coalesce(heartbeat.updated_at_utc, now()) as updated_at_utc
                    from edge_device_runtime_heartbeats heartbeat
                    where not exists (
                        select 1
                        from edge_device_client_states state
                        where state.device_id = heartbeat.device_id
                          and state.client_code = upper(trim(heartbeat.client_code))
                    )
                )
                insert into edge_device_client_states (
                    id,
                    device_id,
                    client_code,
                    version_local_ip_addresses_json,
                    runtime_instance_id,
                    machine_profile,
                    runtime_host_version,
                    runtime_host_api_version,
                    runtime_status,
                    runtime_local_ip_addresses_json,
                    runtime_remote_ip_address,
                    runtime_started_at_utc,
                    last_runtime_heartbeat_at_utc,
                    last_runtime_stopped_at_utc,
                    created_at_utc,
                    updated_at_utc)
                select
                    (substr(hash, 1, 8) || '-' || substr(hash, 9, 4) || '-' || substr(hash, 13, 4) || '-' || substr(hash, 17, 4) || '-' || substr(hash, 21, 12))::uuid,
                    device_id,
                    client_code,
                    '[]'::jsonb,
                    runtime_instance_id,
                    machine_profile,
                    runtime_host_version,
                    runtime_host_api_version,
                    runtime_status,
                    runtime_local_ip_addresses_json,
                    runtime_remote_ip_address,
                    runtime_started_at_utc,
                    last_runtime_heartbeat_at_utc,
                    last_runtime_stopped_at_utc,
                    created_at_utc,
                    updated_at_utc
                from heartbeat_states;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_edge_device_client_states_device",
                table: "edge_device_client_states",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_edge_device_client_states_last_runtime_heartbeat",
                table: "edge_device_client_states",
                column: "last_runtime_heartbeat_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_edge_device_client_states_device_client",
                table: "edge_device_client_states",
                columns: new[] { "device_id", "client_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_device_client_states");
        }
    }
}
