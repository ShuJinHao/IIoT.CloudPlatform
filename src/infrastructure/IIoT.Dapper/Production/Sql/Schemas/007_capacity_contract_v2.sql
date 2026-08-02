alter table hourly_capacity
    add column if not exists schema_version integer not null default 1,
    add column if not exists process_type varchar(32),
    add column if not exists plc_code varchar(64),
    add column if not exists plc_name_is_trusted boolean not null default false;

alter table hourly_capacity
    alter column ok_count drop not null,
    alter column ng_count drop not null,
    alter column plc_name type varchar(128),
    alter column plc_code set default '';

update hourly_capacity
set plc_code = plc_name
where plc_code is null;

alter table hourly_capacity
    alter column plc_code set not null;

drop index if exists ux_hourly_capacity_device_slot_plc;

create unique index if not exists ux_hourly_capacity_device_slot_plc_code
    on hourly_capacity (device_id, date, shift_code, hour, minute, plc_code);

create index if not exists ix_hourly_capacity_device_date_plc_code
    on hourly_capacity (device_id, date, plc_code);

do $$
begin
    if not exists (
        select 1 from pg_constraint where conname = 'ck_hourly_capacity_schema_version'
    ) then
        alter table hourly_capacity
            add constraint ck_hourly_capacity_schema_version
                check (schema_version in (1, 2));
    end if;

    if not exists (
        select 1 from pg_constraint where conname = 'ck_hourly_capacity_quality_pair'
    ) then
        alter table hourly_capacity
            add constraint ck_hourly_capacity_quality_pair
                check (
                    (ok_count is null and ng_count is null)
                    or (
                        ok_count is not null
                        and ng_count is not null
                        and ok_count >= 0
                        and ng_count >= 0
                        and ok_count + ng_count <= total_count
                    )
                );
    end if;

    if not exists (
        select 1 from pg_constraint where conname = 'ck_hourly_capacity_v2_identity'
    ) then
        alter table hourly_capacity
            add constraint ck_hourly_capacity_v2_identity
                check (
                    schema_version = 1
                    or (
                        length(btrim(coalesce(process_type, ''))) > 0
                        and length(btrim(plc_code)) > 0
                        and plc_name_is_trusted
                        and length(btrim(plc_name)) > 0
                    )
                );
    end if;
end $$;
