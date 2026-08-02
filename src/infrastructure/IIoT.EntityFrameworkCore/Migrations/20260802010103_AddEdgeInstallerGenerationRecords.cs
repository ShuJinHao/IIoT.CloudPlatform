using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeInstallerGenerationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_installer_generation_records",
                columns: table => new
                {
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operator_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_runtime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    host_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    package_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    package_size = table.Column<long>(type: "bigint", nullable: false),
                    bindings_json = table.Column<string>(type: "jsonb", nullable: false),
                    plugins_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_installer_generation_records", x => x.generation_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_edge_installer_generation_records_generated_at",
                table: "edge_installer_generation_records",
                column: "generated_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_edge_installer_generation_records_operator",
                table: "edge_installer_generation_records",
                column: "operator_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_installer_generation_records");
        }
    }
}
