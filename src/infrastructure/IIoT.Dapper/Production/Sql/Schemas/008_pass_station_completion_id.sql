alter table pass_station_records
    add column if not exists completion_id varchar(128) null;

create unique index if not exists uq_pass_station_records_device_type_completion
    on pass_station_records (device_id, type_key, completion_id)
    where completion_id is not null;
