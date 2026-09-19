using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

const string supported = """
    {"$type":"TCardItem","Id":"52000000-0000-0000-0000-000000000001","InternalName":"Supported"}
    """;

// Reduced from the downloaded Pantry template that aborts native GetCardMap().
const string unsupported = """
    {"$type":"TCardItem","Id":"c8abacb3-3d98-4266-9842-775a5683ac8c","Auras":{"2":{"Action":{"$type":"TAuraActionCardModifyAttribute","Value":{"$type":"BppTestUnsupportedReferenceValue"}}}}}
    """;
var nativeError = Throws<JsonSerializationException>(() => NativeRead(unsupported));
if (!nativeError.Message.Contains("BppTestUnsupportedReferenceValue"))
    throw new Exception("The native serializer failed for an unrelated reason.");

var cards = CompatibleCardMapReader.ReadRows(
    [unsupported, supported, unsupported],
    out var skipped
);
if (
    skipped != 2
    || cards.Count != 1
    || !cards.ContainsKey(Guid.Parse("52000000-0000-0000-0000-000000000001"))
)
    throw new Exception("An unsupported nested type discarded supported templates.");
Throws<JsonSerializationException>(() => NativeRead(unsupported));
Throws<JsonSerializationException>(() => CompatibleCardMapReader.ReadRows([unsupported], out _));
Throws<JsonReaderException>(() => CompatibleCardMapReader.ReadRows(["{"], out _));
Throws<JsonSerializationException>(() =>
    CompatibleCardMapReader.ReadRows(["{\"Id\":\"bad\"}"], out _)
);
if (
    !CompatibleCardMapReader.IsUnsupportedType(new AggregateException(nativeError))
    || CompatibleCardMapReader.IsUnsupportedType(new AggregateException())
    || CompatibleCardMapReader.IsUnsupportedType(
        new AggregateException(nativeError, new IOException("Database read failed."))
    )
)
    throw new Exception("Mixed bulk failures must propagate.");

var path = Path.Combine(Path.GetTempPath(), $"bpp-card-map-{Guid.NewGuid():N}.db");
try
{
    using (
        var db = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        )
    )
    {
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE cards (Data BLOB NOT NULL); INSERT INTO cards (Data) VALUES ($supported), ($unsupported);";
        cmd.Parameters.AddWithValue("$supported", System.Text.Encoding.UTF8.GetBytes(supported));
        cmd.Parameters.AddWithValue(
            "$unsupported",
            System.Text.Encoding.UTF8.GetBytes(unsupported)
        );
        cmd.ExecuteNonQuery();
    }
    var original = TestInputs.ScratchBytes(path);
    var loaded = CompatibleCardMapReader.Read(path, out var omitted);
    if (loaded.Count != 1 || omitted != 1 || !original.SequenceEqual(TestInputs.ScratchBytes(path)))
        throw new Exception("Read-only database recovery changed the source or lost valid data.");
}
finally
{
    File.Delete(path);
}
PantryCompatibilityTests.Run();
Console.WriteLine(
    "PASS: supported cards survive unsupported nested types; native serializer and source database remain intact."
);

static ITCard? NativeRead(string json) =>
    JsonConvert.DeserializeObject<ITCard>(json, new BazaarJsonSerializerSettings());
static T Throws<T>(Action action)
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
