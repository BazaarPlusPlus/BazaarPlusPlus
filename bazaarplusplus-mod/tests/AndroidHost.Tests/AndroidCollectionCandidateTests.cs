using BazaarPlusPlus.Android.Collection;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// <see cref="AndroidCollectionCatalog.ReadCandidateIds"/>, the pool-thread read that picks which
/// game data rows the catalog loads with the game's per-card <c>GetCardById</c>. Its failure
/// modes:
/// <list type="number">
/// <item>Dropping a card the projection would accept, which leaves it out of the catalog: only
/// a text <c>Type</c> other than Item or Skill, or a <c>SpawningEligibility</c> of Never, may
/// drop a row; a missing or non-text value keeps it.</item>
/// <item>One row that is not JSON failing the whole read: it is kept for the projection to
/// judge.</item>
/// <item>A BLOB column read as SQLite's binary JSON, which drops every row.</item>
/// </list>
/// </summary>
public sealed class AndroidCollectionCandidateTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"bpp-collection-candidates-{Guid.NewGuid():N}"
    );

    [Fact]
    public void Only_other_types_and_never_spawning_cards_are_dropped()
    {
        var item = Guid.NewGuid();
        var skill = Guid.NewGuid();
        var noType = Guid.NewGuid();
        var numericType = Guid.NewGuid();
        var notJson = Guid.NewGuid();
        var encounter = Guid.NewGuid();
        var never = Guid.NewGuid();
        var path = Database(
            (item, """{"Type":"Item","SpawningEligibility":"Always"}"""),
            (skill, """{"Type":"Skill","SpawningEligibility":"GuidOnly"}"""),
            (noType, """{"SpawningEligibility":"Always"}"""),
            (numericType, """{"Type":0}"""),
            (notJson, "{not json"),
            (encounter, """{"Type":"EventEncounter","SpawningEligibility":"Always"}"""),
            (never, """{"Type":"Item","SpawningEligibility":"Never"}""")
        );

        var ids = AndroidCollectionCatalog.ReadCandidateIds(path);

        Assert.Equal(new[] { item, skill, noType, numericType, notJson }.Order(), ids.Order());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // The game's schema: text ids, the card JSON as a BLOB.
    private string Database(params (Guid Id, string Json)[] rows)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "GameData.db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText =
            "CREATE TABLE cards (Id TEXT NOT NULL PRIMARY KEY, Data BLOB NOT NULL) STRICT, WITHOUT ROWID;";
        create.ExecuteNonQuery();
        foreach (var (id, json) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO cards VALUES ($id, $data)";
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.Parameters.AddWithValue("$data", System.Text.Encoding.UTF8.GetBytes(json));
            insert.ExecuteNonQuery();
        }
        return path;
    }
}
