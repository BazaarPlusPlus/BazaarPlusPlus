using System.Collections.Frozen;
using System.Diagnostics;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameShared.Domain.Cards.Socket;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Effect.AuraActions;
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Android.GameInterop;

// Ghost sync runs on a pool thread and cannot query the IL2CPP data manager.
// Read IDs from SQLite because GetCardMap deserializes every card on the Unity thread.
internal sealed class AndroidCardTemplateIndex : IHistoryCardTemplates
{
    private FrozenSet<Guid>? _templates;
    private IntPtr _builtFrom;
    private IntPtr _loadingFrom;
    private Task? _load;

    public bool IsBuilt => Volatile.Read(ref _templates) != null;

    public int Count => Volatile.Read(ref _templates)?.Count ?? 0;

    internal Task? Load => Volatile.Read(ref _load);

    // Before the index is ready, include unknown IDs so Ghost sync does not drop valid templates.
    public bool Contains(Guid templateId)
    {
        var templates = Volatile.Read(ref _templates);
        return templates == null || templates.Contains(templateId);
    }

    public string? SocketEffectAttribute(Guid templateId, CardTier tier)
    {
        var effect = AndroidCardTemplateText.Template(templateId)?.TryCast<TCardSocketEffect>();
        if (effect == null || !AndroidCardFacets.Tier.TryToSource(tier, out var nativeTier))
            return null;
        var auras = effect.GetAuraTemplatesByTier(nativeTier);
        if (auras == null)
            return null;
        foreach (var aura in auras)
        {
            var action = aura?.Action?.TryCast<TAuraActionCardModifyAttribute>();
            if (
                action != null
                && (
                    action.AttributeType == ECardAttributeType.Heated
                    || action.AttributeType == ECardAttributeType.Chilled
                )
            )
                return action.AttributeType.ToString();
        }
        return null;
    }

    public void Freeze(IEnumerable<Guid> templateIds, IntPtr source)
    {
        ArgumentNullException.ThrowIfNull(templateIds);
        Volatile.Write(ref _templates, templateIds.ToFrozenSet());
        _builtFrom = source;
    }

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
