create table if not exists pass_data_injection
(
    id                      uuid            not null,
    device_id               uuid            not null,
    mac_address             varchar(50)     not null,
    client_code             varchar(50)     not null,
    cell_result             varchar(20)     not null,
    completed_time          timestamptz     not null,
    received_at             timestamptz     not null,
    barcode                 varchar(100)    not null,
    pre_injection_time      timestamptz     not null,
    pre_injection_weight    decimal(18, 6)  not null,
    post_injection_time     timestamptz     not null,
    post_injection_weight   decimal(18, 6)  not null,
    injection_volume        decimal(18, 6)  not null,
    primary key (id, completed_time)
);

create index if not exists ix_pass_data_injection_device_time
    on pass_data_injection (device_id, completed_time desc);

create index if not exists ix_pass_data_injection_mac_client_time
    on pass_data_injection (mac_address, client_code, completed_time desc);

create index if not exists ix_pass_data_injection_barcode
    on pass_data_injection (barcode);

-- 幂等性唯一索引:同一设备、同一条码、同一完成时间只能有一条
-- 对应 Dapper 实现里的 ON CONFLICT (device_id, barcode, completed_time) DO NOTHING
create unique index if not exists ux_pass_data_injection_idempotency
    on pass_data_injection (device_id, barcode, completed_time);