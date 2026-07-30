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
                lock table devices in share row exclusive mode;

                create or replace function iiot_fence_legacy_device_client_state()
                returns trigger
                language plpgsql
                as $$
                declare
                    normalized_client_code text :=
                        upper(trim(new.client_code));
                    identity_hash text :=
                        md5(
                            'plc-snapshot:' ||
                            new.id::text ||
                            ':' ||
                            upper(trim(new.client_code)));
                    fence_received_at_utc timestamp with time zone :=
                        statement_timestamp();
                begin
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
                    values (
                        (
                            substr(identity_hash, 1, 8) || '-' ||
                            substr(identity_hash, 9, 4) || '-' ||
                            substr(identity_hash, 13, 4) || '-' ||
                            substr(identity_hash, 17, 4) || '-' ||
                            substr(identity_hash, 21, 12)
                        )::uuid,
                        new.id,
                        normalized_client_code,
                        '[]'::jsonb,
                        '[]'::jsonb,
                        'infinity'::timestamp with time zone,
                        fence_received_at_utc,
                        repeat('0', 64),
                        fence_received_at_utc,
                        fence_received_at_utc)
                    on conflict (device_id, client_code) do nothing;

                    return null;
                end;
                $$;

                drop trigger if exists
                    iiot_fence_legacy_device_client_state_trigger
                    on devices;

                create constraint trigger
                    iiot_fence_legacy_device_client_state_trigger
                after insert on devices
                deferrable initially deferred
                for each row
                execute function iiot_fence_legacy_device_client_state();

                with migration_fence as (
                    select
                        statement_timestamp() as received_at_utc,
                        'infinity'::timestamp with time zone
                            as reported_at_utc
                ),
                registered_device_snapshots as (
                    select
                        device.id as device_id,
                        upper(trim(device.client_code)) as client_code,
                        fence.received_at_utc,
                        fence.reported_at_utc,
                        md5(
                            'plc-snapshot:' ||
                            device.id::text ||
                            ':' ||
                            upper(trim(device.client_code))) as identity_hash
                    from devices device
                    cross join migration_fence fence
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
                    received_at_utc,
                    received_at_utc
                from registered_device_snapshots
                on conflict (device_id, client_code) do nothing;

                with migration_fence as (
                    select
                        statement_timestamp() as received_at_utc,
                        'infinity'::timestamp with time zone
                            as reported_at_utc
                )
                update edge_device_client_states state
                set
                    plc_snapshot_reported_at_utc =
                        coalesce(
                            state.plc_snapshot_reported_at_utc,
                            fence.reported_at_utc),
                    plc_snapshot_received_at_utc =
                        coalesce(
                            state.plc_snapshot_received_at_utc,
                            fence.received_at_utc),
                    plc_snapshot_content_sha256 =
                        coalesce(
                            state.plc_snapshot_content_sha256,
                            repeat('0', 64))
                from migration_fence fence
                where state.plc_snapshot_reported_at_utc is null
                   or state.plc_snapshot_received_at_utc is null
                   or state.plc_snapshot_content_sha256 is null;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop trigger if exists
                    iiot_fence_legacy_device_client_state_trigger
                    on devices;
                drop function if exists
                    iiot_fence_legacy_device_client_state();
                """);

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
