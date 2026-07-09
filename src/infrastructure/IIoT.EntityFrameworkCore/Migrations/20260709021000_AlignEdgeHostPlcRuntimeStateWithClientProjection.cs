using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(IIoTDbContext))]
    [Migration("20260709021000_AlignEdgeHostPlcRuntimeStateWithClientProjection")]
    public partial class AlignEdgeHostPlcRuntimeStateWithClientProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY device_id, UPPER(BTRIM(client_code)), UPPER(BTRIM(plc_code))
                            ORDER BY last_seen_at_utc DESC, updated_at_utc DESC, id DESC
                        ) AS rn
                    FROM edge_host_plc_runtime_states
                )
                DELETE FROM edge_host_plc_runtime_states state
                USING ranked
                WHERE state.id = ranked.id
                  AND ranked.rn > 1;

                UPDATE edge_host_plc_runtime_states
                SET client_code = UPPER(BTRIM(client_code)),
                    plc_code = UPPER(BTRIM(plc_code))
                WHERE client_code <> UPPER(BTRIM(client_code))
                   OR plc_code <> UPPER(BTRIM(plc_code));

                ALTER TABLE edge_host_plc_runtime_states
                    DROP CONSTRAINT IF EXISTS "FK_edge_host_plc_runtime_states_edge_hosts_edge_host_id";

                ALTER TABLE edge_host_plc_runtime_states
                    DROP CONSTRAINT IF EXISTS "FK_edge_host_plc_runtime_states_edge_host_plc_bindings_plc_bin~";

                DROP INDEX IF EXISTS ix_edge_host_plc_runtime_states_edge_host_id;
                DROP INDEX IF EXISTS ix_edge_host_plc_runtime_states_plc_binding_id;

                ALTER TABLE edge_host_plc_runtime_states
                    DROP COLUMN IF EXISTS edge_host_id,
                    DROP COLUMN IF EXISTS plc_binding_id;

                DROP TABLE IF EXISTS edge_host_plc_bindings;
                DROP TABLE IF EXISTS edge_hosts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS edge_hosts (
                    id uuid NOT NULL,
                    device_id uuid NOT NULL,
                    client_code character varying(50) NOT NULL,
                    host_name character varying(128) NOT NULL,
                    enabled boolean NOT NULL,
                    remark character varying(512) NULL,
                    created_at_utc timestamp with time zone NOT NULL,
                    updated_at_utc timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_edge_hosts" PRIMARY KEY (id),
                    CONSTRAINT "FK_edge_hosts_devices_device_id" FOREIGN KEY (device_id) REFERENCES devices (id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_edge_hosts_client_code
                    ON edge_hosts (client_code);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_edge_hosts_device_id
                    ON edge_hosts (device_id);

                CREATE TABLE IF NOT EXISTS edge_host_plc_bindings (
                    id uuid NOT NULL,
                    edge_host_id uuid NOT NULL,
                    plc_code character varying(64) NOT NULL,
                    plc_name character varying(128) NOT NULL,
                    process_id uuid NULL,
                    business_device_id uuid NULL,
                    station_code character varying(128) NULL,
                    protocol character varying(64) NULL,
                    address character varying(256) NULL,
                    enabled boolean NOT NULL,
                    display_order integer NOT NULL,
                    remark character varying(512) NULL,
                    created_at_utc timestamp with time zone NOT NULL,
                    updated_at_utc timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_edge_host_plc_bindings" PRIMARY KEY (id),
                    CONSTRAINT "FK_edge_host_plc_bindings_edge_hosts_edge_host_id" FOREIGN KEY (edge_host_id) REFERENCES edge_hosts (id) ON DELETE CASCADE,
                    CONSTRAINT "FK_edge_host_plc_bindings_devices_business_device_id" FOREIGN KEY (business_device_id) REFERENCES devices (id) ON DELETE SET NULL,
                    CONSTRAINT "FK_edge_host_plc_bindings_mfg_processes_process_id" FOREIGN KEY (process_id) REFERENCES mfg_processes (id) ON DELETE SET NULL
                );

                CREATE INDEX IF NOT EXISTS ix_edge_host_plc_bindings_business_device_id
                    ON edge_host_plc_bindings (business_device_id);
                CREATE INDEX IF NOT EXISTS ix_edge_host_plc_bindings_process_id
                    ON edge_host_plc_bindings (process_id);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_edge_host_plc_bindings_host_plc
                    ON edge_host_plc_bindings (edge_host_id, plc_code);

                ALTER TABLE edge_host_plc_runtime_states
                    ADD COLUMN IF NOT EXISTS edge_host_id uuid NULL,
                    ADD COLUMN IF NOT EXISTS plc_binding_id uuid NULL;

                CREATE INDEX IF NOT EXISTS ix_edge_host_plc_runtime_states_edge_host_id
                    ON edge_host_plc_runtime_states (edge_host_id);
                CREATE INDEX IF NOT EXISTS ix_edge_host_plc_runtime_states_plc_binding_id
                    ON edge_host_plc_runtime_states (plc_binding_id);
                """);
        }
    }
}
