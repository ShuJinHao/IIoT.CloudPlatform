using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceBootstrapSecretHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bootstrap_secret_hash",
                table: "devices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bootstrap_secret_hash",
                table: "devices");
        }
    }
}
