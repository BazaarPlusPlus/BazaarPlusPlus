using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidChoicePedestals
{
    private static int _frame = int.MinValue;
    private static ChoicePedestalProbeOutcome _outcome = ChoicePedestalProbeOutcome.Success(
        ChoicePedestalSnapshot.Empty
    );
    private static string _lastLogged = string.Empty;
    private static long _reads;
    private static long _failures;

    public static long Reads => Interlocked.Read(ref _reads);

    public static long Failures => Interlocked.Read(ref _failures);

    public static ChoicePedestalProbeOutcome Read()
    {
        var frame = Time.frameCount;
        if (frame == _frame)
            return _outcome;
        _frame = frame;
        _reads++;
        try
        {
            var (screen, templateIds) = ReadTemplateIds();
            var snapshot = ChoiceScreenPedestalResolver.Resolve(templateIds);
            _outcome = ChoicePedestalProbeOutcome.Success(snapshot);
            LogChange(screen, templateIds.Count, snapshot);
        }
        catch (Exception exception)
        {
            _failures++;
            _outcome = ChoicePedestalProbeOutcome.Failure(
                EncounterProbeFailureReason.ChoiceResolutionException,
                exception
            );
        }
        return _outcome;
    }

    private static (string Screen, IReadOnlyList<Guid> TemplateIds) ReadTemplateIds()
    {
        var runState = Data.CurrentState;
        var appState = AppState.CurrentState;
        if (runState == null || appState == null)
            return ("none", Array.Empty<Guid>());

        if (appState.TryCast<PedestalState>() != null)
        {
            return Guid.TryParse(runState.CurrentEncounterId, out var current)
                ? ("pedestal", new[] { current })
                : ("pedestal", Array.Empty<Guid>());
        }

        var screen =
            appState.TryCast<ChoiceState>() != null ? "choice"
            : appState.TryCast<EncounterState>() != null ? "encounter"
            : appState.TryCast<LevelUpState>() != null ? "level_up"
            : null;
        var selection = runState.SelectionSet;
        if (screen == null || selection == null || selection.Count == 0)
            return (screen ?? "other", Array.Empty<Guid>());

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var templateIds = new List<Guid>(selection.Count);
        foreach (var entry in selection)
        {
            if (string.IsNullOrEmpty(entry))
                continue;
            // An entry is a live instance id (e.g. "ped_XtbgKux"), or already a template id.
            if (Guid.TryParse(entry, out var direct))
                templateIds.Add(direct);
            else
                wanted.Add(entry);
        }
        var entities = Data.Entities;
        if (wanted.Count > 0 && entities != null)
        {
            foreach (var card in entities.Values)
            {
                var instanceId = card?.InstanceId?.Value;
                if (instanceId == null || !wanted.Remove(instanceId))
                    continue;
                if (Guid.TryParse(card!.TemplateId.ToString(), out var templateId))
                    templateIds.Add(templateId);
                if (wanted.Count == 0)
                    break;
            }
        }
        return (screen, templateIds);
    }

    private static void LogChange(string screen, int ids, ChoicePedestalSnapshot snapshot)
    {
        var names = string.Join(",", snapshot.EnchantmentTypeNames);
        var key = $"{screen}|{snapshot.Kind}|{names}";
        if (key == _lastLogged)
            return;
        _lastLogged = key;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.enchant_preview.pedestal_changed"),
            ("screen", screen),
            ("template_ids", ids),
            ("kind", snapshot.Kind),
            ("enchantments", names)
        );
    }
}
