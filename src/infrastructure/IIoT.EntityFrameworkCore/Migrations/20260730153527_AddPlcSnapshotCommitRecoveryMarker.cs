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
