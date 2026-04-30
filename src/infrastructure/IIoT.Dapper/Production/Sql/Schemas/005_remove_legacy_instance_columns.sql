-- One-time compatibility migration for older schemas that still contain
-- mac_address/client_code in record tables.

drop index if exists ix_device_logs_mac_client_time;
drop index if exists ix_hourly_capacity_mac_client_date;
drop index if exists ux_hourly_capacity_instance_slot_plc;

alter table device_logs
    drop column if exists mac_address,
    drop column if exists client_code;

alter table hourly_capacity
    drop column if exists mac_address,
    drop column if exists client_code;

create unique index if not exists ux_hourly_capacity_device_slot_plc
    on hourly_capacity (device_id, date, shift_code, hour, minute, plc_name);
