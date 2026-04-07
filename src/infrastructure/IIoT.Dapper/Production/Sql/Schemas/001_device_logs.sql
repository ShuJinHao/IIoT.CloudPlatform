create table if not exists device_logs
(
    id           uuid        not null,
    device_id    uuid        not null,
    mac_address  varchar(50) not null,
    client_code  varchar(50) not null,
    level        varchar(20) not null,
    message      text        not null,
    log_time     timestamptz not null,
    received_at  timestamptz not null,
    primary key (id, log_time)
);

create index if not exists ix_device_logs_device_time
    on device_logs (device_id, log_time desc);

create index if not exists ix_device_logs_mac_client_time
    on device_logs (mac_address, client_code, log_time desc);

create index if not exists ix_device_logs_level
    on device_logs (level);