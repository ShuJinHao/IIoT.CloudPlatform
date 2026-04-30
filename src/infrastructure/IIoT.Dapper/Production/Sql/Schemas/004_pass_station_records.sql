create table if not exists pass_station_records
(
    id uuid not null,
    device_id uuid not null,
    type_key varchar(64) not null,
    barcode varchar(128) not null,
    cell_result varchar(32) not null,
    completed_time timestamp without time zone not null,
    received_at timestamp without time zone not null,
    deduplication_key varchar(128) not null,
    payload_jsonb jsonb not null,
    constraint pk_pass_station_records primary key (id, completed_time),
    constraint uq_pass_station_records_type_dedup unique (type_key, deduplication_key, completed_time)
);

create index if not exists ix_pass_station_records_type_completed
    on pass_station_records (type_key, completed_time desc);

create index if not exists ix_pass_station_records_type_device_completed
    on pass_station_records (type_key, device_id, completed_time desc);

create index if not exists ix_pass_station_records_type_barcode
    on pass_station_records (type_key, barcode);
