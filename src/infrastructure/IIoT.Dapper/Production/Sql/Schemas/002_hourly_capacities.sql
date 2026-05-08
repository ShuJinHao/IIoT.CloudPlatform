create table if not exists hourly_capacity
(
    id            uuid        not null,
    device_id     uuid        not null,
    date          date        not null,
    shift_code    varchar(10) not null,
    hour          int         not null,
    minute        int         not null,
    time_label    varchar(20) not null,
    total_count   int         not null,
    ok_count      int         not null,
    ng_count      int         not null,
    plc_name      varchar(50) not null default '',
    reported_at   timestamptz not null,
    primary key (id, date)
);

create index if not exists ix_hourly_capacity_device_date
    on hourly_capacity (device_id, date);

create index if not exists ix_hourly_capacity_date_slot
    on hourly_capacity (date, hour, minute);

create unique index if not exists ux_hourly_capacity_device_slot_plc
    on hourly_capacity (device_id, date, shift_code, hour, minute, plc_name);
