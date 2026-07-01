using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeReleaseApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edge_release_api_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PermissionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_release_api_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_edge_release_api_keys_ExpiresAtUtc",
                table: "edge_release_api_keys",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_edge_release_api_keys_KeyHash",
                table: "edge_release_api_keys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_release_api_keys_LastUsedAtUtc",
                table: "edge_release_api_keys",
                column: "LastUsedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_edge_release_api_keys_Name",
                table: "edge_release_api_keys",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_release_api_keys_Status",
                table: "edge_release_api_keys",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edge_release_api_keys");
        }
    }
}
