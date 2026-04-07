create table if not exists hourly_capacity
(
    id            uuid        not null,
    device_id     uuid        not null,
    mac_address   varchar(50) not null,
    client_code   varchar(50) not null,
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
    primary key (id)
);

-- 主力查询索引:按单台设备 + 日期范围查询(日/月/年/班次汇总都走这个)
create index if not exists ix_hourly_capacity_device_date
    on hourly_capacity (device_id, date);

-- 按实例身份反查(上位机自己查自己的数据)
create index if not exists ix_hourly_capacity_mac_client_date
    on hourly_capacity (mac_address, client_code, date);

-- 唯一键:同一身份下,同一时段、同一 PLC 的产能槽位只有一条
create unique index if not exists ux_hourly_capacity_instance_slot_plc
    on hourly_capacity (mac_address, client_code, date, shift_code, hour, minute, plc_name);