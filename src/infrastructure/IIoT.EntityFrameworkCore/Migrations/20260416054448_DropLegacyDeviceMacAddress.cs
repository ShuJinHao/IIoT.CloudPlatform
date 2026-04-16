using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyDeviceMacAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE devices
                DROP COLUMN IF EXISTS mac_address;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE devices
                ADD COLUMN IF NOT EXISTS mac_address character varying(50) NOT NULL DEFAULT '';
                ALTER TABLE devices
                ALTER COLUMN mac_address DROP DEFAULT;
                """);
        }
    }
}
