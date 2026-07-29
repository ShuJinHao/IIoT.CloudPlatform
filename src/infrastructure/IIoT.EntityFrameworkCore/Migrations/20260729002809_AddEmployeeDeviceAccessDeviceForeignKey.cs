using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDeviceAccessDeviceForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    orphan_count bigint;
                BEGIN
                    SELECT COUNT(*)
                    INTO orphan_count
                    FROM employee_device_accesses access
                    LEFT JOIN devices device ON device.id = access.device_id
                    WHERE device.id IS NULL;

                    IF orphan_count > 0 THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23503',
                            MESSAGE = format(
                                '人员设备授权迁移预检失败：发现 %s 条孤儿设备授权；未执行删除、补设备或数据改写，请先定向清理后重试。',
                                orphan_count);
                    END IF;
                END $$;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_employee_device_accesses_devices_device_id",
                table: "employee_device_accesses",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employee_device_accesses_devices_device_id",
                table: "employee_device_accesses");
        }
    }
}
