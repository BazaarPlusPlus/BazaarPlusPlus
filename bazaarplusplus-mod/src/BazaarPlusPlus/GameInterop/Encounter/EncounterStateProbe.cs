#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Read-only encounter state module. Keep the id and choice reads cheap;
/// target-selection reads are isolated behind <see cref="GetTargetingState"/>.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe, ITypedEncounterStateProbe
{
    private int _encounterIdsFrame = int.MinValue;
    private int _choicePedestalFrame = int.MinValue;
    private int _targetingFrame = int.MinValue;
    private EncounterIdsProbeOutcome _encounterIdsOutcome = EncounterIdsProbeOutcome.Success(
        EncounterIdsSnapshot.Empty
    );
    private ChoicePedestalProbeOutcome _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
        ChoicePedestalSnapshot.Empty
    );
    private EncounterTargetingProbeOutcome _targetingOutcome =
        EncounterTargetingProbeOutcome.Success(EncounterTargetingSnapshot.Empty);

    public EncounterIdsSnapshot GetEncounterIds()
    {
        return GetEncounterIdsOutcome().Snapshot;
    }

    public EncounterIdsProbeOutcome GetEncounterIdsOutcome()
    {
        var frame = Time.frameCount;
        if (_encounterIdsFrame == frame)
            return _encounterIdsOutcome;

        _encounterIdsOutcome = ReadEncounterIds();
        _encounterIdsFrame = frame;
        return _encounterIdsOutcome;
    }

    public ChoicePedestalSnapshot GetChoicePedestal()
    {
        return GetChoicePedestalOutcome().Snapshot;
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
                _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
                    AppState.CurrentState is PedestalState
                    && idsOutcome.Snapshot.CurrentEncounterTemplateId.HasValue
                        ? CreateChoicePedestalSnapshot(
                            ChoiceScreenPedestalResolver.ResolveDetailedFromTemplateIds(
                                new[] { idsOutcome.Snapshot.CurrentEncounterTemplateId.Value }
                            )
                        )
                        : ChoicePedestalSnapshot.Empty
                );
            }
            else
            {
                var choice = ChoiceScreenPedestalResolver.ResolveDetailedFromTemplateIds(
                    idsOutcome.Snapshot.ChoiceSelectionTemplateIds
                );
                _choicePedestalOutcome = ChoicePedestalProbeOutcome.Success(
                    CreateChoicePedestalSnapshot(choice)
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

    public EncounterTargetingSnapshot GetTargetingState()
    {
        return GetTargetingStateOutcome().Snapshot;
    }

    public EncounterTargetingProbeOutcome GetTargetingStateOutcome()
    {
        var frame = Time.frameCount;
        if (_targetingFrame == frame)
            return _targetingOutcome;

        _targetingOutcome = ReadTargetingState();
        _targetingFrame = frame;
        return _targetingOutcome;
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

    private static EncounterTargetingProbeOutcome ReadTargetingState()
    {
        try
        {
            var appState = AppState.CurrentState;
            var filterOutcome = InteractionFilterProbe.ReadCurrentFilter();
            if (!filterOutcome.IsSuccess)
            {
                return EncounterTargetingProbeOutcome.Failure(
                    filterOutcome.FailureReason,
                    filterOutcome.Exception
                );
            }
            var isPedestalState = appState is PedestalState;
            var pedestalEligible = new HashSet<string>();
            if (appState is PedestalState ped)
            {
                var pedestalOutcome = PedestalEligibilityProbe.ReadEligibleInstanceIds(ped);
                if (!pedestalOutcome.IsSuccess)
                {
                    return EncounterTargetingProbeOutcome.Failure(
                        pedestalOutcome.FailureReason,
                        pedestalOutcome.Exception
                    );
                }
                pedestalEligible = pedestalOutcome.InstanceIds;
            }

            return EncounterTargetingProbeOutcome.Success(
                new EncounterTargetingSnapshot
                {
                    InteractionFilterTemplateIds = filterOutcome.TemplateIds,
                    PedestalEligibleInstanceIds = pedestalEligible,
                    IsPedestalState = isPedestalState,
                }
            );
        }
        catch (Exception ex)
        {
            return EncounterTargetingProbeOutcome.Failure(
                EncounterProbeFailureReason.TargetingReadException,
                ex
            );
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

    private static ChoicePedestalSnapshot CreateChoicePedestalSnapshot(
        ChoiceScreenPedestalResult choice
    )
    {
        return new ChoicePedestalSnapshot
        {
            Kind = choice.Kind,
            EnchantmentTypeNames = choice.EnchantmentTypeNames,
        };
    }
}
