#nullable enable
namespace BazaarPlusPlus.Storage.Sqlite;

/// <summary>The SQLite journal modes a host may choose for the run-log database.</summary>
public enum SqliteJournalMode
{
    /// <summary>Write-ahead logging: <c>-wal</c> and <c>-shm</c> sidecars, concurrent readers.</summary>
    Wal,

    /// <summary>Rollback journal: no sidecar outlives a transaction.</summary>
    Delete,
}

/// <summary>
/// How a host opens the run-log database. The database file and its schema are the same on every
/// host; only the journal mode differs, because WAL depends on a shared <c>-shm</c> mapping that
/// not every filesystem under a Data Root is known to support.
/// </summary>
public sealed class SqliteOpenOptions
{
    /// <summary>The desktop hosts' choice, and the default when a store is given no options.</summary>
    public static SqliteOpenOptions Default { get; } = new(SqliteJournalMode.Wal);

    public SqliteOpenOptions(SqliteJournalMode journalMode) => JournalMode = journalMode;

    public SqliteJournalMode JournalMode { get; }

    internal string JournalModePragma =>
        JournalMode switch
        {
            SqliteJournalMode.Wal => "PRAGMA journal_mode = WAL;",
            SqliteJournalMode.Delete => "PRAGMA journal_mode = DELETE;",
            _ => throw new ArgumentOutOfRangeException(
                nameof(JournalMode),
                JournalMode,
                "Unknown SQLite journal mode."
            ),
        };
}
