if object_id(N'dbo.events', N'U') is null
create table dbo.events (
    position bigint identity(1, 1) not null constraint events_pk primary key,
    stream_category nvarchar(100) not null,
    stream_id uniqueidentifier not null,
    version bigint not null constraint events_version_positive check (version > 0),
    event_type nvarchar(200) not null,
    data nvarchar(max) not null constraint events_data_is_json check (isjson(data) = 1),
    recorded_at datetimeoffset not null constraint events_recorded_at_default default sysdatetimeoffset(),
    constraint events_stream_version_key unique (stream_category, stream_id, version)
);

if object_id(N'dbo.snapshots', N'U') is null
create table dbo.snapshots (
    stream_category nvarchar(100) not null,
    stream_id uniqueidentifier not null,
    version bigint not null constraint snapshots_version_positive check (version > 0),
    data nvarchar(max) not null constraint snapshots_data_is_json check (isjson(data) = 1),
    taken_at datetimeoffset not null constraint snapshots_taken_at_default default sysdatetimeoffset(),
    constraint snapshots_pk primary key (stream_category, stream_id)
);
