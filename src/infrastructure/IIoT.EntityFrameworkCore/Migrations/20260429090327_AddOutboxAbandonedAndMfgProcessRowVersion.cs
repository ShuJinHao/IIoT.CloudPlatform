using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxAbandonedAndMfgProcessRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_dispatch",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "abandoned_at_utc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "mfg_processes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_abandoned",
                table: "outbox_messages",
                columns: new[] { "abandoned_at_utc", "last_attempted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatch",
                table: "outbox_messages",
                columns: new[] { "processed_at_utc", "abandoned_at_utc", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_abandoned",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_dispatch",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "abandoned_at_utc",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "mfg_processes");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatch",
                table: "outbox_messages",
                columns: new[] { "processed_at_utc", "occurred_at_utc" });
        }
    }
}
