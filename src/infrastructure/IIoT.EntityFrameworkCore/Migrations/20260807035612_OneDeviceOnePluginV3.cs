using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class OneDeviceOnePluginV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail closed before canonicalizing existing business keys. The
            // runtime removes every Unicode whitespace character and compares
            // case-insensitively; migration must never merge conflicting rows.
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM mfg_processes
                        GROUP BY upper(translate(process_code,
                            chr(9)||chr(10)||chr(11)||chr(12)||chr(13)||chr(32)||
                            chr(133)||chr(160)||chr(5760)||chr(8192)||chr(8193)||
                            chr(8194)||chr(8195)||chr(8196)||chr(8197)||chr(8198)||
                            chr(8199)||chr(8200)||chr(8201)||chr(8202)||chr(8232)||
                            chr(8233)||chr(8239)||chr(8287)||chr(12288), ''))
                        HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'OneDeviceOnePluginV3 preflight: normalized process_code collision; resolve explicitly before migration';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM devices
                        GROUP BY upper(translate(device_name,
                            chr(9)||chr(10)||chr(11)||chr(12)||chr(13)||chr(32)||
                            chr(133)||chr(160)||chr(5760)||chr(8192)||chr(8193)||
                            chr(8194)||chr(8195)||chr(8196)||chr(8197)||chr(8198)||
                            chr(8199)||chr(8200)||chr(8201)||chr(8202)||chr(8232)||
                            chr(8233)||chr(8239)||chr(8287)||chr(12288), ''))
                        HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'OneDeviceOnePluginV3 preflight: normalized device_name collision; resolve explicitly before migration';
                    END IF;
                END
                $migration$;

                UPDATE mfg_processes
                SET process_code = upper(translate(process_code,
                    chr(9)||chr(10)||chr(11)||chr(12)||chr(13)||chr(32)||
                    chr(133)||chr(160)||chr(5760)||chr(8192)||chr(8193)||
                    chr(8194)||chr(8195)||chr(8196)||chr(8197)||chr(8198)||
                    chr(8199)||chr(8200)||chr(8201)||chr(8202)||chr(8232)||
                    chr(8233)||chr(8239)||chr(8287)||chr(12288), ''));
                """);

            migrationBuilder.DropIndex(
                name: "ix_devices_device_name",
                table: "devices");

            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "edge_host_plc_runtime_states",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plc_snapshot_configuration_version",
                table: "edge_device_client_states",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "plc_snapshot_explicit_clear",
                table: "edge_device_client_states",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "plc_snapshot_is_authoritative",
                table: "edge_device_client_states",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "package_sha256",
                table: "edge_device_client_plugin_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "data_capabilities_json",
                table: "edge_client_release_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "file_manifest_sha256",
                table: "edge_client_release_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dependency_closure_sha256",
                table: "edge_client_release_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dependency_host_version",
                table: "edge_client_release_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dependency_host_file_manifest_sha256",
                table: "edge_client_release_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_document_ref",
                table: "edge_client_release_components",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "data_capabilities_json",
                table: "edge_client_release_components",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "file_manifest_sha256",
                table: "edge_client_release_components",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "manifest_schema_version",
                table: "edge_client_release_components",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "supported_process_type",
                table: "edge_client_release_components",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "was_ever_device_bound",
                table: "edge_client_release_components",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "normalized_device_name",
                table: "devices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE devices
                SET normalized_device_name = upper(translate(device_name,
                    chr(9)||chr(10)||chr(11)||chr(12)||chr(13)||chr(32)||
                    chr(133)||chr(160)||chr(5760)||chr(8192)||chr(8193)||
                    chr(8194)||chr(8195)||chr(8196)||chr(8197)||chr(8198)||
                    chr(8199)||chr(8200)||chr(8201)||chr(8202)||chr(8232)||
                    chr(8233)||chr(8239)||chr(8287)||chr(12288), ''));
                ALTER TABLE devices ALTER COLUMN normalized_device_name SET NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "device_plugin_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_release_component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bound_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_plugin_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_plugin_bindings_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_plugin_bindings_edge_client_release_components_clien~",
                        column: x => x.client_release_component_id,
                        principalTable: "edge_client_release_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "edge_installer_pending_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PluginVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivationStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyProcessId = table.Column<int>(type: "integer", nullable: true),
                    ReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivationReplayCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_installer_pending_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edge_installer_pending_credentials_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_edge_installer_pending_credentials_edge_installer_generatio~",
                        column: x => x.GenerationId,
                        principalTable: "edge_installer_generation_records",
                        principalColumn: "generation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_devices_normalized_device_name",
                table: "devices",
                column: "normalized_device_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_device_plugin_bindings_component",
                table: "device_plugin_bindings",
                column: "client_release_component_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_device_plugin_bindings_device",
                table: "device_plugin_bindings",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_installer_pending_credentials_ClientCode_Status_Expire~",
                table: "edge_installer_pending_credentials",
                columns: new[] { "ClientCode", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_edge_installer_pending_credentials_DeviceId",
                table: "edge_installer_pending_credentials",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_edge_installer_pending_credentials_GenerationId_DeviceId",
                table: "edge_installer_pending_credentials",
                columns: new[] { "GenerationId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_plugin_bindings");

            migrationBuilder.DropTable(
                name: "edge_installer_pending_credentials");

            migrationBuilder.DropIndex(
                name: "ux_devices_normalized_device_name",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "enabled",
                table: "edge_host_plc_runtime_states");

            migrationBuilder.DropColumn(
                name: "plc_snapshot_configuration_version",
                table: "edge_device_client_states");

            migrationBuilder.DropColumn(
                name: "plc_snapshot_explicit_clear",
                table: "edge_device_client_states");

            migrationBuilder.DropColumn(
                name: "plc_snapshot_is_authoritative",
                table: "edge_device_client_states");

            migrationBuilder.DropColumn(
                name: "package_sha256",
                table: "edge_device_client_plugin_versions");

            migrationBuilder.DropColumn(
                name: "data_capabilities_json",
                table: "edge_client_release_versions");

            migrationBuilder.DropColumn(
                name: "file_manifest_sha256",
                table: "edge_client_release_versions");

            migrationBuilder.DropColumn(
                name: "dependency_closure_sha256",
                table: "edge_client_release_versions");

            migrationBuilder.DropColumn(
                name: "dependency_host_version",
                table: "edge_client_release_versions");

            migrationBuilder.DropColumn(
                name: "dependency_host_file_manifest_sha256",
                table: "edge_client_release_versions");

            migrationBuilder.DropColumn(
                name: "business_document_ref",
                table: "edge_client_release_components");

            migrationBuilder.DropColumn(
                name: "data_capabilities_json",
                table: "edge_client_release_components");

            migrationBuilder.DropColumn(
                name: "file_manifest_sha256",
                table: "edge_client_release_components");

            migrationBuilder.DropColumn(
                name: "manifest_schema_version",
                table: "edge_client_release_components");

            migrationBuilder.DropColumn(
                name: "supported_process_type",
                table: "edge_client_release_components");

            migrationBuilder.DropColumn(
                name: "was_ever_device_bound",
                table: "edge_client_release_components");

            migrationBuilder.DropColumn(
                name: "normalized_device_name",
                table: "devices");

            migrationBuilder.CreateIndex(
                name: "ix_devices_device_name",
                table: "devices",
                column: "device_name",
                unique: true);
        }
    }
}
