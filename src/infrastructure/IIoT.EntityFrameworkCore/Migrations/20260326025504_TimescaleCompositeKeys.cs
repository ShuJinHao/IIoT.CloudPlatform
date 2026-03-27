using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class TimescaleCompositeKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_pass_data_injection",
                table: "pass_data_injection");

            migrationBuilder.DropPrimaryKey(
                name: "PK_device_logs",
                table: "device_logs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_pass_data_injection",
                table: "pass_data_injection",
                columns: new[] { "id", "completed_time" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_device_logs",
                table: "device_logs",
                columns: new[] { "id", "log_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_pass_data_injection",
                table: "pass_data_injection");

            migrationBuilder.DropPrimaryKey(
                name: "PK_device_logs",
                table: "device_logs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_pass_data_injection",
                table: "pass_data_injection",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_device_logs",
                table: "device_logs",
                column: "id");
        }
    }
}
