create table if not exists hourly_capacity
(
    id uuid primary key,
    device_id uuid not null,
    mac_address varchar(100) not null,
    client_code varchar(100) not null,
    date date not null,
    shift_code varchar(50) not null,
    hour integer not null,
    minute integer not null,
    time_label varchar(50) not null,
    total_count integer not null,
    ok_count integer not null,
    ng_count integer not null,
    plc_name varchar(100) not null default '',
    reported_at timestamp with time zone not null
);

create index if not exists idx_hourly_capacity_device_id
    on hourly_capacity(device_id);

create index if not exists idx_hourly_capacity_mac_client
    on hourly_capacity(mac_address, client_code);

create index if not exists idx_hourly_capacity_date_shift
    on hourly_capacity(date, shift_code);

create unique index if not exists ux_hourly_capacity_instance_slot_plc
    on hourly_capacity(mac_address, client_code, date, shift_code, hour, minute, plc_name);