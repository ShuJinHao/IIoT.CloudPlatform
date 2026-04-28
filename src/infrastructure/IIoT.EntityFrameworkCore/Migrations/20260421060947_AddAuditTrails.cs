using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditTrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_trails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorEmployeeNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OperationType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetIdOrKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_trails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_trails_ExecutedAtUtc",
                table: "audit_trails",
                column: "ExecutedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_trails_OperationType_TargetType",
                table: "audit_trails",
                columns: new[] { "OperationType", "TargetType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_trails");
        }
    }
}
