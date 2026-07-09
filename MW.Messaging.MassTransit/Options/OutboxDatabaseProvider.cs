namespace MW.Messaging.MassTransit.Options;

/// <summary>
/// Database provider used by the MassTransit Entity Framework Core transactional outbox
/// to select the correct store / optimistic-concurrency lock provider.
/// Must match the database backing the <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// that owns the outbox tables.
/// </summary>
public enum OutboxDatabaseProvider
{
    /// <summary>Microsoft SQL Server (default).</summary>
    SqlServer = 0,

    /// <summary>PostgreSQL.</summary>
    PostgreSql = 1
}
