#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Serialization;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BazaarPlusPlus.GameInterop.StaticCards;

internal static class CompatibleCardMapReader
{
    internal static Dictionary<Guid, ITCard> Read(string path, out int unsupportedCount)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data FROM cards";
        using var reader = command.ExecuteReader();
        return ReadRows(Rows(), out unsupportedCount);

        IEnumerable<string> Rows()
        {
            while (reader.Read())
                yield return System.Text.Encoding.UTF8.GetString((byte[])reader.GetValue(0));
        }
    }

    internal static Dictionary<Guid, ITCard> ReadRows(
        IEnumerable<string> rows,
        out int unsupportedCount
    )
    {
        var cards = new Dictionary<Guid, ITCard>();
        var serializer = JsonSerializer.Create(new BazaarJsonSerializerSettings());
        CompatibleCardValueConverter.Install(serializer);
        unsupportedCount = 0;
        foreach (var json in rows)
        {
            try
            {
                using var text = new StringReader(json);
                using var reader = new JsonTextReader(text);
                var card =
                    serializer.Deserialize<ITCard>(reader)
                    ?? throw new JsonSerializationException("Card template was null.");
                cards.Add(card.Id, card);
            }
            catch (JsonSerializationException error) when (IsUnsupportedType(error))
            {
                // A server-added type cannot be rendered by this client. Keep the rest of the
                // catalog without changing native type mappings or the game's shared card cache.
                unsupportedCount++;
            }
        }
        if (cards.Count == 0 && unsupportedCount > 0)
            throw new JsonSerializationException("No card templates are supported by this client.");
        return cards;
    }

    internal static bool IsUnsupportedType(Exception error)
    {
        if (error is AggregateException aggregate)
        {
            var errors = aggregate.Flatten().InnerExceptions;
            return errors.Count > 0 && errors.All(IsUnsupportedType);
        }

        const string prefix = "Unknown type or missing type information: ";
        return error is JsonSerializationException
            && error.Message.StartsWith(prefix, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(error.Message.Substring(prefix.Length));
    }
}
