alter table pass_station_records
    add column if not exists completion_id varchar(128) null;

-- TimescaleDB requires every unique index on a hypertable to include its
-- partitioning column. CompletionId is a business idempotency key whose
-- uniqueness must not depend on completed_time, so keep the claim in a
-- normal PostgreSQL table and atomically claim it before inserting the
-- hypertable row.
create table if not exists pass_station_completion_claims
(
    device_id uuid not null,
    type_key varchar(64) not null,
    completion_id varchar(128) not null,
    constraint pk_pass_station_completion_claims
        primary key (device_id, type_key, completion_id)
);

-- Recover safely if an earlier, unreleased initialization created the
-- pre-hypertable unique index before Timescale conversion failed.
insert into pass_station_completion_claims (device_id, type_key, completion_id)
select device_id, type_key, completion_id
from pass_station_records
where completion_id is not null
on conflict do nothing;

drop index if exists uq_pass_station_records_device_type_completion;

create index if not exists ix_pass_station_completion_claims_device
    on pass_station_completion_claims (device_id);
