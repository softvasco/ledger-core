using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.ReadModels;

internal sealed class ReadModelContext(DbContextOptions<ReadModelContext> options) : DbContext(options)
{
    public DbSet<AccountBalanceRow> Balances => Set<AccountBalanceRow>();

    public DbSet<StatementLineRow> StatementLines => Set<StatementLineRow>();

    public DbSet<CheckpointRow> Checkpoints => Set<CheckpointRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountBalanceRow>(balance =>
        {
            balance.ToTable("account_balances");
            balance.HasKey(b => b.AccountId);
            balance.Property(b => b.AccountId).HasColumnName("account_id");
            balance.Property(b => b.Iban).HasColumnName("iban").HasMaxLength(34);
            balance.Property(b => b.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsUnicode(false);
            balance.Property(b => b.Status).HasColumnName("status").HasMaxLength(20);
            balance.Property(b => b.Balance).HasColumnName("balance").HasPrecision(19, 4);
            balance.Property(b => b.Version).HasColumnName("version");
        });

        modelBuilder.Entity<StatementLineRow>(line =>
        {
            line.ToTable("statement_lines");
            line.HasKey(l => new { l.AccountId, l.Version });
            line.Property(l => l.AccountId).HasColumnName("account_id");
            line.Property(l => l.Version).HasColumnName("version");
            line.Property(l => l.Position).HasColumnName("position");
            line.Property(l => l.Kind).HasColumnName("kind").HasMaxLength(20);
            line.Property(l => l.Amount).HasColumnName("amount").HasPrecision(19, 4);
            line.Property(l => l.BalanceAfter).HasColumnName("balance_after").HasPrecision(19, 4);
            line.Property(l => l.OccurredAt).HasColumnName("occurred_at");
        });

        modelBuilder.Entity<CheckpointRow>(checkpoint =>
        {
            checkpoint.ToTable("projection_checkpoints");
            checkpoint.HasKey(c => c.Projection);
            checkpoint.Property(c => c.Projection).HasColumnName("projection").HasMaxLength(100);
            checkpoint.Property(c => c.Position).HasColumnName("position");
        });
    }
}

internal sealed class AccountBalanceRow
{
    public Guid AccountId { get; set; }

    public string Iban { get; set; } = "";

    public string Currency { get; set; } = "";

    public string Status { get; set; } = "";

    public decimal Balance { get; set; }

    public long Version { get; set; }
}

/// <summary>One deposit or withdrawal; a withdrawal has a negative amount.</summary>
internal sealed class StatementLineRow
{
    public Guid AccountId { get; set; }

    public long Version { get; set; }

    public long Position { get; set; }

    public string Kind { get; set; } = "";

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

internal sealed class CheckpointRow
{
    public string Projection { get; set; } = "";

    public long Position { get; set; }
}
