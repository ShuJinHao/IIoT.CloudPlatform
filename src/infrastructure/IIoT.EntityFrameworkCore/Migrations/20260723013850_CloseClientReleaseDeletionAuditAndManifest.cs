using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class CloseClientReleaseDeletionAuditAndManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "cleanup_completed_at_utc",
                table: "edge_client_release_component_deletions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cleanup_result_json",
                table: "edge_client_release_component_deletions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "audit_trails",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_trails_IdempotencyKey",
                table: "audit_trails",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_trails_IdempotencyKey",
                table: "audit_trails");

            migrationBuilder.DropColumn(
                name: "cleanup_completed_at_utc",
                table: "edge_client_release_component_deletions");

            migrationBuilder.DropColumn(
                name: "cleanup_result_json",
                table: "edge_client_release_component_deletions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "audit_trails");
        }
    }
}
