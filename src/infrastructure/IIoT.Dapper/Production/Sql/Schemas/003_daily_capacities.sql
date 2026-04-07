create table if not exists daily_capacities
(
    id uuid primary key,
    device_id uuid not null,
    mac_address varchar(100) not null,
    client_code varchar(100) not null,
    date date not null,
    shift_code varchar(50) not null,
    total_count integer not null,
    ok_count integer not null,
    ng_count integer not null,
    reported_at timestamp with time zone not null
);

create index if not exists idx_daily_capacities_device_id
    on daily_capacities(device_id);

create index if not exists idx_daily_capacities_mac_client
    on daily_capacities(mac_address, client_code);

create index if not exists idx_daily_capacities_date_shift
    on daily_capacities(date, shift_code);

create unique index if not exists ux_daily_capacities_instance_day
    on daily_capacities(mac_address, client_code, date, shift_code);