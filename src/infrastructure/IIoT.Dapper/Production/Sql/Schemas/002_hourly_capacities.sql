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
    ok_count      int,
    ng_count      int,
    schema_version int        not null default 1,
    process_type  varchar(32),
    plc_code      varchar(64) not null default '',
    plc_name      varchar(128) not null default '',
    plc_name_is_trusted boolean not null default false,
    reported_at   timestamptz not null,
    primary key (id, date)
);

create index if not exists ix_hourly_capacity_device_date
    on hourly_capacity (device_id, date);

create index if not exists ix_hourly_capacity_date_slot
    on hourly_capacity (date, hour, minute);
