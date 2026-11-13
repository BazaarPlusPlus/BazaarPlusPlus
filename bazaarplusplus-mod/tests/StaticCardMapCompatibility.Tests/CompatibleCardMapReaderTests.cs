using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Xunit;

namespace StaticCardMapCompatibility.Tests;

// The capsule's checks, with the same calls and messages: supported cards survive unsupported
// nested types; the native serializer and the source database stay intact.
public sealed class CompatibleCardMapReaderTests
{
    private const string Supported = """
        {"$type":"TCardItem","Id":"52000000-0000-0000-0000-000000000001","InternalName":"Supported"}
        """;

    // Reduced from the downloaded Pantry template that aborts native GetCardMap().
    private const string Unsupported = """
        {"$type":"TCardItem","Id":"c8abacb3-3d98-4266-9842-775a5683ac8c","Auras":{"2":{"Action":{"$type":"TAuraActionCardModifyAttribute","Value":{"$type":"BppTestUnsupportedReferenceValue"}}}}}
        """;

    // One sequence: the native failure, the rows that survive it, the native serializer
    // afterwards, and the bulk failures built from that native error.
    [Fact]
    public void Unsupported_nested_types_discard_only_their_rows()
    {
        var nativeError = Throws<JsonSerializationException>(() => NativeRead(Unsupported));
        if (!nativeError.Message.Contains("BppTestUnsupportedReferenceValue"))
            throw new Exception("The native serializer failed for an unrelated reason.");

        var cards = CompatibleCardMapReader.ReadRows(
            [Unsupported, Supported, Unsupported],
            out var skipped
        );
        if (
            skipped != 2
            || cards.Count != 1
            || !cards.ContainsKey(Guid.Parse("52000000-0000-0000-0000-000000000001"))
        )
            throw new Exception("An unsupported nested type discarded supported templates.");
        Throws<JsonSerializationException>(() => NativeRead(Unsupported));

        if (
            !CompatibleCardMapReader.IsUnsupportedType(new AggregateException(nativeError))
            || CompatibleCardMapReader.IsUnsupportedType(new AggregateException())
            || CompatibleCardMapReader.IsUnsupportedType(
                new AggregateException(nativeError, new IOException("Database read failed."))
            )
        )
            throw new Exception("Mixed bulk failures must propagate.");
    }

    [Fact]
    public void Rows_with_nothing_supported_still_throw()
    {
        Throws<JsonSerializationException>(() =>
            CompatibleCardMapReader.ReadRows([Unsupported], out _)
        );
        Throws<JsonReaderException>(() => CompatibleCardMapReader.ReadRows(["{"], out _));
        Throws<JsonSerializationException>(() =>
            CompatibleCardMapReader.ReadRows(["{\"Id\":\"bad\"}"], out _)
        );
    }

    [Fact]
    public void Read_only_database_recovery_keeps_the_source_and_the_valid_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bpp-card-map-{Guid.NewGuid():N}.db");
        try
        {
            using (
                var db = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = path,
                        Pooling = false,
                    }.ToString()
                )
            )
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE cards (Data BLOB NOT NULL); INSERT INTO cards (Data) VALUES ($supported), ($unsupported);";
                cmd.Parameters.AddWithValue(
                    "$supported",
                    System.Text.Encoding.UTF8.GetBytes(Supported)
                );
                cmd.Parameters.AddWithValue(
                    "$unsupported",
                    System.Text.Encoding.UTF8.GetBytes(Unsupported)
                );
                cmd.ExecuteNonQuery();
            }
            var original = TestInputs.ScratchBytes(path);
            var loaded = CompatibleCardMapReader.Read(path, out var omitted);
            if (
                loaded.Count != 1
                || omitted != 1
                || !original.SequenceEqual(TestInputs.ScratchBytes(path))
            )
                throw new Exception(
                    "Read-only database recovery changed the source or lost valid data."
                );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ITCard? NativeRead(string json) =>
        JsonConvert.DeserializeObject<ITCard>(json, new BazaarJsonSerializerSettings());

    private static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T error)
        {
            return error;
        }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
