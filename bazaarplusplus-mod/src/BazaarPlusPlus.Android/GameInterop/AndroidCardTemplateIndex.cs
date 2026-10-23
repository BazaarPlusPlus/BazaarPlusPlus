using System.Collections.Frozen;
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The card template ids of the game's static data, frozen into a managed set so Ghost sync can
/// ask whether a downloaded snapshot's template exists from the pool thread it runs on, where
/// the static data manager must not be called. When History opens, <see cref="EnsureBuilt"/>
/// reads the manager's game-data file path on the main thread and a pool thread reads the ids
/// from that file's <c>cards</c> table (<see cref="ReadTemplateIds"/>); the set is rebuilt when
/// the static data manager changes. The manager's own <c>GetCardMap</c> would deserialize every
/// card on the main thread, which froze the game for 26 s on the emulator. Until the set exists
/// <see cref="Contains"/> answers true: an unknown template is counted rather than dropped, and a
/// lookup never builds the set.
/// </summary>
internal sealed class AndroidCardTemplateIndex
{
    private FrozenSet<Guid>? _templates;
    private IntPtr _builtFrom;
    private IntPtr _loadingFrom;
    private Task? _load;

    public bool IsBuilt => Volatile.Read(ref _templates) != null;

    public int Count => Volatile.Read(ref _templates)?.Count ?? 0;

    /// <summary>The running or last read, for tests and the device probe.</summary>
    internal Task? Load => Volatile.Read(ref _load);

    /// <summary>Any thread: whether the template exists, or true before the set is built.</summary>
    public bool Contains(Guid templateId)
    {
        var templates = Volatile.Read(ref _templates);
        return templates == null || templates.Contains(templateId);
    }

    /// <summary>Replaces the set; the managed half of <see cref="EnsureBuilt"/>.</summary>
    public void Freeze(IEnumerable<Guid> templateIds, IntPtr source)
    {
        ArgumentNullException.ThrowIfNull(templateIds);
        Volatile.Write(ref _templates, templateIds.ToFrozenSet());
        _builtFrom = source;
    }

    /// <summary>
    /// Main thread: starts the read for the current static data manager, once per manager
    /// instance. A manager that is not ready yet leaves the set unbuilt; a failure is logged and
    /// degrades to counting every template.
    /// </summary>
    public void EnsureBuilt()
    {
        try
        {
            if (!Il2CppTheBazaar.Data.IsManagerCreated())
                return;
            var manager = Il2CppTheBazaar.Data.GetStatic();
            if (manager == null)
                return;
            var source = manager.Pointer;
            if ((IsBuilt && source == _builtFrom) || source == Volatile.Read(ref _loadingFrom))
                return;
            var path = manager._dbPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                Failed("no_database_path", null);
                return;
            }
            Volatile.Write(ref _loadingFrom, source);
            Volatile.Write(ref _load, Task.Run(() => BuildFrom(path, source)));
        }
        catch (Exception exception)
        {
            Failed(exception.GetType().Name, exception);
        }
    }

    /// <summary>Any thread: the ids of a game-data file's <c>cards</c> table.</summary>
    internal static List<Guid> ReadTemplateIds(string path)
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
        command.CommandText = "SELECT Id FROM cards";
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read())
        {
            if (Guid.TryParse(reader.GetString(0), out var id) && id != Guid.Empty)
                ids.Add(id);
        }
        return ids;
    }

    private void BuildFrom(string path, IntPtr source)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var ids = ReadTemplateIds(path);
            Freeze(ids, source);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.template_index_built"),
                ("templates", ids.Count),
                ("elapsed_ms", clock.ElapsedMilliseconds)
            );
        }
        catch (Exception exception)
        {
            Failed(exception.GetType().Name, exception);
        }
        finally
        {
            Interlocked.CompareExchange(ref _loadingFrom, IntPtr.Zero, source);
        }
    }

    private static void Failed(string reasonCode, Exception? exception)
    {
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.Android,
            "android.history.template_index_failed",
            storm: ["reason_code"]
        );
        if (exception == null)
            BppLog.WarnEvent(logEvent, ("reason_code", reasonCode));
        else
            BppLog.WarnEvent(logEvent, exception, ("reason_code", reasonCode));
    }
}
