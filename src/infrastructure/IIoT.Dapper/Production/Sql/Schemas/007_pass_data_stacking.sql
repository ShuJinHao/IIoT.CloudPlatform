create table if not exists pass_data_stacking
(
    id              uuid            not null,
    device_id       uuid            not null,
    barcode         varchar(100)    not null,
    tray_code       varchar(100)    not null,
    sequence_no     integer         not null,
    layer_count     integer         not null,
    cell_result     varchar(20)     not null,
    completed_time  timestamptz     not null,
    received_at     timestamptz     not null,
    primary key (id, completed_time)
);

create index if not exists ix_pass_data_stacking_device_time
    on pass_data_stacking (device_id, completed_time desc);

create index if not exists ix_pass_data_stacking_barcode
    on pass_data_stacking (barcode);

create unique index if not exists ux_pass_data_stacking_idempotency
    on pass_data_stacking (device_id, barcode, completed_time);
