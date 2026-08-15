#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.Storage.Sqlite;

public static class SqliteUtcInstant
{
    private const string SqliteDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static DateTimeOffset Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("SQLite UTC instant is empty.");

        if (
            DateTimeOffset.TryParseExact(
                value,
                SqliteDateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var sqliteValue
            )
        )
            return sqliteValue;
        if (
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var offsetValue
            )
        )
            return offsetValue;
        throw new FormatException("SQLite UTC instant is invalid.");
    }
}
