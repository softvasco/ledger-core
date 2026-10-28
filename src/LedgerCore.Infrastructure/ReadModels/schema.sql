if object_id(N'dbo.account_balances', N'U') is null
create table dbo.account_balances (
    account_id uniqueidentifier not null constraint account_balances_pk primary key,
    iban nvarchar(34) not null,
    currency char(3) not null,
    status nvarchar(20) not null,
    balance decimal(19, 4) not null,
    version bigint not null
);

if object_id(N'dbo.statement_lines', N'U') is null
create table dbo.statement_lines (
    account_id uniqueidentifier not null,
    version bigint not null,
    position bigint not null,
    kind nvarchar(20) not null,
    amount decimal(19, 4) not null,
    balance_after decimal(19, 4) not null,
    occurred_at datetimeoffset not null,
    constraint statement_lines_pk primary key (account_id, version)
);

if object_id(N'dbo.projection_checkpoints', N'U') is null
create table dbo.projection_checkpoints (
    projection nvarchar(100) not null constraint projection_checkpoints_pk primary key,
    position bigint not null
);
