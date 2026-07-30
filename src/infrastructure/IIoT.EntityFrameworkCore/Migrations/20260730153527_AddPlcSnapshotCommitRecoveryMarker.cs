using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPlcSnapshotCommitRecoveryMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plc_snapshot_content_sha256",
                table: "edge_device_client_states",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "plc_snapshot_received_at_utc",
                table: "edge_device_client_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "plc_snapshot_reported_at_utc",
                table: "edge_device_client_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                with legacy_plc_snapshots as (
                    select
                        runtime.device_id,
                        upper(trim(runtime.client_code)) as client_code,
                        min(runtime.created_at_utc) as created_at_utc,
                        max(runtime.updated_at_utc) as received_at_utc,
                        max(runtime.updated_at_utc) as reported_at_utc,
                        md5(
                            'plc-snapshot:' ||
                            runtime.device_id::text ||
                            ':' ||
                            upper(trim(runtime.client_code))) as identity_hash
                    from edge_host_plc_runtime_states runtime
                    group by
                        runtime.device_id,
                        upper(trim(runtime.client_code))
                )
                insert into edge_device_client_states (
                    id,
                    device_id,
                    client_code,
                    version_local_ip_addresses_json,
                    runtime_local_ip_addresses_json,
                    plc_snapshot_reported_at_utc,
                    plc_snapshot_received_at_utc,
                    plc_snapshot_content_sha256,
                    created_at_utc,
                    updated_at_utc)
                select
                    (
                        substr(identity_hash, 1, 8) || '-' ||
                        substr(identity_hash, 9, 4) || '-' ||
                        substr(identity_hash, 13, 4) || '-' ||
                        substr(identity_hash, 17, 4) || '-' ||
                        substr(identity_hash, 21, 12)
                    )::uuid,
                    device_id,
                    client_code,
                    '[]'::jsonb,
                    '[]'::jsonb,
                    reported_at_utc,
                    received_at_utc,
                    repeat('0', 64),
                    created_at_utc,
                    greatest(received_at_utc, reported_at_utc)
                from legacy_plc_snapshots
                on conflict (device_id, client_code) do nothing;

                with legacy_plc_snapshots as (
                    select
                        runtime.device_id,
                        upper(trim(runtime.client_code)) as client_code,
                        max(runtime.updated_at_utc) as received_at_utc,
                        max(runtime.updated_at_utc) as reported_at_utc
                    from edge_host_plc_runtime_states runtime
                    group by
                        runtime.device_id,
                        upper(trim(runtime.client_code))
                )
                update edge_device_client_states state
                set
                    plc_snapshot_reported_at_utc =
                        coalesce(
                            state.plc_snapshot_reported_at_utc,
                            snapshot.reported_at_utc),
                    plc_snapshot_received_at_utc =
                        coalesce(
                            state.plc_snapshot_received_at_utc,
                            snapshot.received_at_utc),
                    plc_snapshot_content_sha256 =
                        coalesce(
                            state.plc_snapshot_content_sha256,
                            repeat('0', 64))
                from legacy_plc_snapshots snapshot
                where state.device_id = snapshot.device_id
                  and upper(trim(state.client_code)) = snapshot.client_code;

                update edge_device_client_states
                set
                    plc_snapshot_reported_at_utc =
                        coalesce(
                            plc_snapshot_reported_at_utc,
                            updated_at_utc),
                    plc_snapshot_received_at_utc =
                        coalesce(
                            plc_snapshot_received_at_utc,
                            updated_at_utc),
                    plc_snapshot_content_sha256 =
                        coalesce(
                            plc_snapshot_content_sha256,
                            repeat('0', 64))
                where plc_snapshot_reported_at_utc is null
                   or plc_snapshot_received_at_utc is null
                   or plc_snapshot_content_sha256 is null;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "plc_snapshot_content_sha256",
                table: "edge_device_client_states");

            migrationBuilder.DropColumn(
                name: "plc_snapshot_received_at_utc",
                table: "edge_device_client_states");

            migrationBuilder.DropColumn(
                name: "plc_snapshot_reported_at_utc",
                table: "edge_device_client_states");
        }
    }
}
