create table if not exists events (
    position bigint generated always as identity primary key,
    stream_category text not null,
    stream_id uuid not null,
    version bigint not null check (version > 0),
    event_type text not null,
    data jsonb not null,
    recorded_at timestamptz not null default now(),
    constraint events_stream_version_key unique (stream_category, stream_id, version)
);

create table if not exists snapshots (
    stream_category text not null,
    stream_id uuid not null,
    version bigint not null check (version > 0),
    data jsonb not null,
    taken_at timestamptz not null default now(),
    primary key (stream_category, stream_id)
);
