#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Read-only encounter state module. Keep the id and choice reads cheap.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
    private readonly Guid[] _pedestalEncounterTemplateId = new Guid[1];
    private int _encounterIdsFrame = int.MinValue;
    private int _choicePedestalFrame = int.MinValue;
    private EncounterIdsProbeOutcome _encounterIdsOutcome = EncounterIdsProbeOutcome.Success(
        EncounterIdsSnapshot.Empty
    );
    private ChoicePedestalProbeOutcome _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
        ChoicePedestalSnapshot.Empty
    );

    public EncounterIdsProbeOutcome GetEncounterIdsOutcome()
    {
        var frame = Time.frameCount;
        if (_encounterIdsFrame == frame)
            return _encounterIdsOutcome;

        _encounterIdsOutcome = ReadEncounterIds();
        _encounterIdsFrame = frame;
        return _encounterIdsOutcome;
    }

    public ChoicePedestalProbeOutcome GetChoicePedestalOutcome()
    {
        var frame = Time.frameCount;
        if (_choicePedestalFrame == frame)
            return _choicePedestalOutcome;

        try
        {
            var idsOutcome = GetEncounterIdsOutcome();
            if (!idsOutcome.IsSuccess)
            {
                _choicePedestalOutcome = ChoicePedestalProbeOutcome.Failure(
                    idsOutcome.FailureReason,
                    idsOutcome.Exception
                );
            }
            else if (!idsOutcome.Snapshot.IsSelectionState)
            {
                var pedestalTemplateId = idsOutcome.Snapshot.CurrentEncounterTemplateId;
                if (AppState.CurrentState is PedestalState && pedestalTemplateId.HasValue)
                {
                    // Resolve does not retain its input, so one slot serves every read.
                    _pedestalEncounterTemplateId[0] = pedestalTemplateId.Value;
                    _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
                        ChoiceScreenPedestalResolver.Resolve(_pedestalEncounterTemplateId)
                    );
                }
                else
                {
                    _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
                        ChoicePedestalSnapshot.Empty
                    );
                }
            }
            else
            {
                _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
                    ChoiceScreenPedestalResolver.Resolve(
                        idsOutcome.Snapshot.ChoiceSelectionTemplateIds
                    )
                );
            }
        }
        catch (Exception ex)
        {
            _choicePedestalOutcome = ChoicePedestalProbeOutcome.Failure(
                EncounterProbeFailureReason.ChoiceResolutionException,
                ex
            );
        }

        _choicePedestalFrame = frame;
        return _choicePedestalOutcome;
    }

    private static EncounterIdsProbeOutcome ReadEncounterIds()
    {
        try
        {
            var runState = Data.CurrentState;
            var appState = AppState.CurrentState;

            var currentEncounterId = runState?.CurrentEncounterId;
            var currentEncounterTemplateId = TryParseTemplateId(currentEncounterId);
            var isChoiceState = appState is ChoiceState;
            var isSelectionState =
                isChoiceState || appState is EncounterState || appState is LevelUpState;

            if (
                !isSelectionState
                || runState?.SelectionSet == null
                || runState.SelectionSet.Count == 0
            )
            {
                return EncounterIdsProbeOutcome.Success(
                    new EncounterIdsSnapshot
                    {
                        CurrentEncounterId = currentEncounterId,
                        CurrentEncounterTemplateId = currentEncounterTemplateId,
                        IsChoiceState = isChoiceState,
                        IsSelectionState = isSelectionState,
                        ChoiceSelectionEntryIds = Array.Empty<string>(),
                        ChoiceSelectionTemplateIds = Array.Empty<Guid>(),
                    }
                );
            }

            var entryIds = new List<string>(runState.SelectionSet.Count);
            var templateIds = new List<Guid>(runState.SelectionSet.Count);
            foreach (var entry in runState.SelectionSet)
            {
                if (string.IsNullOrEmpty(entry))
                    continue;

                entryIds.Add(entry);
                var templateId = ResolveTemplateId(entry);
                if (templateId.HasValue && templateId.Value != Guid.Empty)
                    templateIds.Add(templateId.Value);
            }

            return EncounterIdsProbeOutcome.Success(
                new EncounterIdsSnapshot
                {
                    CurrentEncounterId = currentEncounterId,
                    CurrentEncounterTemplateId = currentEncounterTemplateId,
                    IsChoiceState = isChoiceState,
                    IsSelectionState = true,
                    ChoiceSelectionEntryIds =
                        entryIds.Count == 0 ? Array.Empty<string>() : entryIds.ToArray(),
                    ChoiceSelectionTemplateIds =
                        templateIds.Count == 0 ? Array.Empty<Guid>() : templateIds.ToArray(),
                }
            );
        }
        catch (Exception ex)
        {
            return EncounterIdsProbeOutcome.Failure(ex);
        }
    }

    // SelectionSet entries are live instance ids (e.g. "ped_XtbgKux"); resolve each
    // through Data.Entities to its stable template id, which PedestalEnchantCatalog
    // classifies. The client cannot read the obfuscated pedestal Behavior, so the
    // template id is the only stable handle. Falls through to a raw template GUID.
    private static Guid? ResolveTemplateId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var entities = Data.Entities;
        if (
            entities != null
            && entities.TryGetValue(new InstanceId(id), out var card)
            && card != null
        )
        {
            return card.TemplateId;
        }

        return Guid.TryParse(id, out var guid) ? guid : null;
    }

    private static Guid? TryParseTemplateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return Guid.TryParse(id, out var guid) ? guid : null;
    }
}
