using IIoT.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(IIoTDbContext))]
    [Migration("20260508090000_AddUploadReceiveRegistrationDeviceLastSeenIndex")]
    public partial class AddUploadReceiveRegistrationDeviceLastSeenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_upload_receive_registrations_device_last_seen",
                table: "upload_receive_registrations",
                columns: new[] { "device_id", "last_seen_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_upload_receive_registrations_device_last_seen",
                table: "upload_receive_registrations");
        }
    }
}
