create table if not exists pass_data_injection
(
    id uuid primary key,
    device_id uuid not null,
    mac_address varchar(100) not null,
    client_code varchar(100) not null,
    cell_result varchar(50) not null,
    completed_time timestamp with time zone not null,
    received_at timestamp with time zone not null,
    barcode varchar(200) not null,
    pre_injection_time timestamp with time zone not null,
    pre_injection_weight numeric(18,6) not null,
    post_injection_time timestamp with time zone not null,
    post_injection_weight numeric(18,6) not null,
    injection_volume numeric(18,6) not null
);

create index if not exists idx_pass_data_injection_device_id
    on pass_data_injection(device_id);

create index if not exists idx_pass_data_injection_mac_client
    on pass_data_injection(mac_address, client_code);

create index if not exists idx_pass_data_injection_completed_time
    on pass_data_injection(completed_time desc);

create index if not exists idx_pass_data_injection_barcode
    on pass_data_injection(barcode);