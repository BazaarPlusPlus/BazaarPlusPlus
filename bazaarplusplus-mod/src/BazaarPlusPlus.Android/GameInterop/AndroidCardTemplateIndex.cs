using System.Collections.Frozen;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The card template ids of the game's static data, frozen into a managed set so Ghost sync can
/// ask whether a downloaded snapshot's template exists from the pool thread it runs on, where
/// the static data manager must not be called. The set is built on the main thread when History
/// opens (<see cref="EnsureBuilt"/>), and rebuilt when the static data manager changes. Until it
/// exists <see cref="Contains"/> answers true: an unknown template is counted rather than
/// dropped, and a lookup never builds the set, which would be the off-thread IL2CPP call this
/// index exists to prevent.
/// </summary>
internal sealed class AndroidCardTemplateIndex
{
    private FrozenSet<Guid>? _templates;
    private IntPtr _builtFrom;

    public bool IsBuilt => Volatile.Read(ref _templates) != null;

    public int Count => Volatile.Read(ref _templates)?.Count ?? 0;

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
    /// Main thread: builds the set from the game's static data, once per manager instance. A
    /// manager that is not ready yet leaves the set unbuilt; a failure is logged and degrades to
    /// counting every template.
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
            if (IsBuilt && manager.Pointer == _builtFrom)
                return;
            var cards = manager.GetCardMap();
            if (cards == null)
                return;
            var ids = new List<Guid>(cards.Count);
            foreach (var entry in cards)
            {
                if (Guid.TryParse(entry.Key.ToString(), out var id) && id != Guid.Empty)
                    ids.Add(id);
            }
            Freeze(ids, manager.Pointer);
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.history.template_index_built"),
                ("templates", ids.Count)
            );
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.history.template_index_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
        }
    }
}
