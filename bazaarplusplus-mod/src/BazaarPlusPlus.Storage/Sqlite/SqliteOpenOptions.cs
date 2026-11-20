#nullable enable
namespace BazaarPlusPlus.Storage.Sqlite;

public enum SqliteJournalMode
{
    Wal,

    Delete,
}

// WAL requires a shared -shm mapping that some Data Root filesystems may not support.
public sealed class SqliteOpenOptions
{
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
