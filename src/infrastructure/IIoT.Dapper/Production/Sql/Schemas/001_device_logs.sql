create table if not exists device_logs
(
    id uuid primary key,
    device_id uuid not null,
    mac_address varchar(100) not null,
    client_code varchar(100) not null,
    level varchar(50) not null,
    message text not null,
    log_time timestamp with time zone not null,
    received_at timestamp with time zone not null
);

create index if not exists idx_device_logs_device_id
    on device_logs(device_id);

create index if not exists idx_device_logs_mac_client
    on device_logs(mac_address, client_code);

create index if not exists idx_device_logs_log_time
    on device_logs(log_time desc);