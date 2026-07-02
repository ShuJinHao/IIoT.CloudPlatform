using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeClientReleaseComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_client_release_components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    component_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    icon_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    accent_color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_release_components", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "edge_client_release_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    min_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    max_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletion_reason = table.Column<string>(type: "text", nullable: true),
                    deletion_failure = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_release_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_client_release_versions_edge_client_release_components~",
                        column: x => x.component_id,
                        principalTable: "edge_client_release_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edge_client_release_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_release_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_edge_client_release_artifacts_edge_client_release_versions_~",
                        column: x => x.release_version_id,
                        principalTable: "edge_client_release_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_edge_client_release_artifacts_version_path",
                table: "edge_client_release_artifacts",
                columns: new[] { "release_version_id", "artifact_kind", "relative_path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_release_components_catalog",
                table: "edge_client_release_components",
                columns: new[] { "channel", "target_runtime", "component_kind" });

            migrationBuilder.CreateIndex(
                name: "ux_edge_client_release_components_identity",
                table: "edge_client_release_components",
                columns: new[] { "component_kind", "component_key", "channel", "target_runtime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_edge_client_release_versions_status",
                table: "edge_client_release_versions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_edge_client_release_versions_component_version",
                table: "edge_client_release_versions",
                columns: new[] { "component_id", "version" },
                unique: true);

            migrationBuilder.Sql("""
                with host_components as (
                    select
                        channel,
                        target_runtime,
                        min(created_at_utc) as created_at_utc,
                        max(created_at_utc) as updated_at_utc,
                        md5('client-release-component:host:EdgeHost:' || channel || ':' || target_runtime) as hash
                    from edge_client_host_releases
                    group by channel, target_runtime
                )
                insert into edge_client_release_components (
                    id,
                    component_kind,
                    component_key,
                    display_name,
                    description,
                    icon_kind,
                    accent_color,
                    channel,
                    target_runtime,
                    created_at_utc,
                    updated_at_utc)
                select
                    (substr(hash, 1, 8) || '-' || substr(hash, 9, 4) || '-' || substr(hash, 13, 4) || '-' || substr(hash, 17, 4) || '-' || substr(hash, 21, 12))::uuid,
                    'Host',
                    'EdgeHost',
                    'Edge Host',
                    null,
                    null,
                    null,
                    channel,
                    target_runtime,
                    created_at_utc,
                    updated_at_utc
                from host_components
                on conflict do nothing;

                with plugin_components as (
                    select
                        module_id,
                        channel,
                        target_runtime,
                        (array_agg(display_name order by coalesce(published_at_utc, created_at_utc) desc))[1] as display_name,
                        (array_agg(description order by coalesce(published_at_utc, created_at_utc) desc))[1] as description,
                        (array_agg(icon_kind order by coalesce(published_at_utc, created_at_utc) desc))[1] as icon_kind,
                        (array_agg(accent_color order by coalesce(published_at_utc, created_at_utc) desc))[1] as accent_color,
                        min(created_at_utc) as created_at_utc,
                        max(created_at_utc) as updated_at_utc,
                        md5('client-release-component:plugin:' || module_id || ':' || channel || ':' || target_runtime) as hash
                    from edge_client_plugin_releases
                    group by module_id, channel, target_runtime
                )
                insert into edge_client_release_components (
                    id,
                    component_kind,
                    component_key,
                    display_name,
                    description,
                    icon_kind,
                    accent_color,
                    channel,
                    target_runtime,
                    created_at_utc,
                    updated_at_utc)
                select
                    (substr(hash, 1, 8) || '-' || substr(hash, 9, 4) || '-' || substr(hash, 13, 4) || '-' || substr(hash, 17, 4) || '-' || substr(hash, 21, 12))::uuid,
                    'Plugin',
                    module_id,
                    coalesce(nullif(display_name, ''), module_id),
                    description,
                    icon_kind,
                    accent_color,
                    channel,
                    target_runtime,
                    created_at_utc,
                    updated_at_utc
                from plugin_components
                on conflict do nothing;

                insert into edge_client_release_versions (
                    id,
                    component_id,
                    version,
                    host_api_version,
                    min_host_version,
                    max_host_version,
                    target_framework,
                    download_url,
                    sha256,
                    package_size,
                    release_notes,
                    dependencies_json,
                    status,
                    signature,
                    publisher,
                    created_at_utc,
                    published_at_utc,
                    deleted_at_utc,
                    deletion_reason,
                    deletion_failure)
                select
                    host.id,
                    component.id,
                    host.version,
                    host.host_api_version,
                    null,
                    null,
                    host.target_framework,
                    host.download_url,
                    host.sha256,
                    host.package_size,
                    host.release_notes,
                    '[]'::jsonb,
                    host.status,
                    host.signature,
                    host.publisher,
                    host.created_at_utc,
                    host.published_at_utc,
                    null,
                    null,
                    null
                from edge_client_host_releases host
                inner join edge_client_release_components component
                    on component.component_kind = 'Host'
                    and component.component_key = 'EdgeHost'
                    and component.channel = host.channel
                    and component.target_runtime = host.target_runtime
                on conflict do nothing;

                insert into edge_client_release_versions (
                    id,
                    component_id,
                    version,
                    host_api_version,
                    min_host_version,
                    max_host_version,
                    target_framework,
                    download_url,
                    sha256,
                    package_size,
                    release_notes,
                    dependencies_json,
                    status,
                    signature,
                    publisher,
                    created_at_utc,
                    published_at_utc,
                    deleted_at_utc,
                    deletion_reason,
                    deletion_failure)
                select
                    plugin.id,
                    component.id,
                    plugin.version,
                    plugin.host_api_version,
                    plugin.min_host_version,
                    plugin.max_host_version,
                    plugin.target_framework,
                    plugin.download_url,
                    plugin.sha256,
                    plugin.package_size,
                    plugin.release_notes,
                    plugin.dependencies_json,
                    plugin.status,
                    plugin.signature,
                    plugin.publisher,
                    plugin.created_at_utc,
                    plugin.published_at_utc,
                    null,
                    null,
                    null
                from edge_client_plugin_releases plugin
                inner join edge_client_release_components component
                    on component.component_kind = 'Plugin'
                    and component.component_key = plugin.module_id
                    and component.channel = plugin.channel
                    and component.target_runtime = plugin.target_runtime
                on conflict do nothing;

                with host_artifacts as (
                    select
                        id as release_version_id,
                        'InstallerDirectory' as artifact_kind,
                        'installers/' || channel || '/' || version as relative_path,
                        null::text as sha256,
                        null::bigint as size,
                        created_at_utc
                    from edge_client_host_releases
                    union all
                    select
                        id as release_version_id,
                        'ManifestFile' as artifact_kind,
                        case
                            when download_url like '/edge-updates/%' then substring(download_url from 15)
                            when download_url ~ '^https?://[^/]+/edge-updates/' then regexp_replace(download_url, '^https?://[^/]+/edge-updates/', '')
                            else null
                        end as relative_path,
                        sha256,
                        package_size,
                        created_at_utc
                    from edge_client_host_releases
                ),
                plugin_artifacts as (
                    select
                        id as release_version_id,
                        'PackageFile' as artifact_kind,
                        case
                            when download_url like '/edge-updates/%' then substring(download_url from 15)
                            when download_url ~ '^https?://[^/]+/edge-updates/' then regexp_replace(download_url, '^https?://[^/]+/edge-updates/', '')
                            else null
                        end as relative_path,
                        sha256,
                        package_size,
                        created_at_utc
                    from edge_client_plugin_releases
                    union all
                    select
                        id as release_version_id,
                        'PluginPackageDirectory' as artifact_kind,
                        regexp_replace(
                            case
                                when download_url like '/edge-updates/%' then substring(download_url from 15)
                                when download_url ~ '^https?://[^/]+/edge-updates/' then regexp_replace(download_url, '^https?://[^/]+/edge-updates/', '')
                                else null
                            end,
                            '/[^/]+$',
                            '') as relative_path,
                        null::text as sha256,
                        null::bigint as size,
                        created_at_utc
                    from edge_client_plugin_releases
                ),
                artifacts as (
                    select * from host_artifacts
                    union all
                    select * from plugin_artifacts
                ),
                artifact_ids as (
                    select
                        *,
                        md5(release_version_id::text || ':' || artifact_kind || ':' || relative_path) as hash
                    from artifacts
                    where relative_path is not null
                      and relative_path <> ''
                )
                insert into edge_client_release_artifacts (
                    id,
                    release_version_id,
                    artifact_kind,
                    relative_path,
                    sha256,
                    size,
                    created_at_utc)
                select
                    (substr(hash, 1, 8) || '-' || substr(hash, 9, 4) || '-' || substr(hash, 13, 4) || '-' || substr(hash, 17, 4) || '-' || substr(hash, 21, 12))::uuid,
                    release_version_id,
                    artifact_kind,
                    relative_path,
                    sha256,
                    size,
                    created_at_utc
                from artifact_ids
                on conflict do nothing;
                """);

            migrationBuilder.DropTable(
                name: "edge_client_host_releases");

            migrationBuilder.DropTable(
                name: "edge_client_plugin_releases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_client_release_artifacts");

            migrationBuilder.DropTable(
                name: "edge_client_release_versions");

            migrationBuilder.DropTable(
                name: "edge_client_release_components");

            migrationBuilder.CreateTable(
                name: "edge_client_host_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletion_failure = table.Column<string>(type: "text", nullable: true),
                    deletion_reason = table.Column<string>(type: "text", nullable: true),
                    download_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    package_size = table.Column<long>(type: "bigint", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    publisher = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signature = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    target_framework = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
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
                    accent_color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletion_failure = table.Column<string>(type: "text", nullable: true),
                    deletion_reason = table.Column<string>(type: "text", nullable: true),
                    dependencies_json = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    download_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    host_api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    icon_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    max_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    min_host_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    module_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    package_size = table.Column<long>(type: "bigint", nullable: false),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    publisher = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    release_notes = table.Column<string>(type: "text", nullable: true),
                    sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signature = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    target_framework = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_client_plugin_releases", x => x.id);
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
        }
    }
}
