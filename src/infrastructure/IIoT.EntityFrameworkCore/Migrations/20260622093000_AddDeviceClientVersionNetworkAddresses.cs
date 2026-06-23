using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceClientVersionNetworkAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "local_ip_addresses_json",
                table: "edge_device_client_version_snapshots",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "remote_ip_address",
                table: "edge_device_client_version_snapshots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "local_ip_addresses_json",
                table: "edge_device_client_version_snapshots");

            migrationBuilder.DropColumn(
                name: "remote_ip_address",
                table: "edge_device_client_version_snapshots");
        }
    }
}
