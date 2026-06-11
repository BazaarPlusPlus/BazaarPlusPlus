#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Read-only encounter state module. Keep the id and choice reads cheap;
/// target-selection reads are isolated behind <see cref="GetTargetingState"/>.</summary>
internal sealed class EncounterStateProbe : IEncounterStateProbe
{
    private int _encounterIdsFrame = int.MinValue;
    private int _choicePedestalFrame = int.MinValue;
    private int _targetingFrame = int.MinValue;
    private EncounterIdsSnapshot _encounterIdsSnapshot = EncounterIdsSnapshot.Empty;
    private ChoicePedestalSnapshot _choicePedestalSnapshot = ChoicePedestalSnapshot.Empty;
    private EncounterTargetingSnapshot _targetingSnapshot = EncounterTargetingSnapshot.Empty;

    public EncounterIdsSnapshot GetEncounterIds()
    {
        var frame = Time.frameCount;
        if (_encounterIdsFrame == frame)
            return _encounterIdsSnapshot;

        _encounterIdsSnapshot = ReadEncounterIds();
        _encounterIdsFrame = frame;
        return _encounterIdsSnapshot;
    }

    public ChoicePedestalSnapshot GetChoicePedestal()
    {
        var frame = Time.frameCount;
        if (_choicePedestalFrame == frame)
            return _choicePedestalSnapshot;

        var ids = GetEncounterIds();
        if (!ids.IsSelectionState)
        {
            _choicePedestalSnapshot =
                AppState.CurrentState is PedestalState && ids.CurrentEncounterTemplateId.HasValue
                    ? CreateChoicePedestalSnapshot(
                        ChoiceScreenPedestalResolver.ResolveDetailedFromTemplateIds(
                            new[] { ids.CurrentEncounterTemplateId.Value }
                        )
                    )
                    : ChoicePedestalSnapshot.Empty;
            _choicePedestalFrame = frame;
            return _choicePedestalSnapshot;
        }

        var choice = ChoiceScreenPedestalResolver.ResolveDetailedFromTemplateIds(
            ids.ChoiceSelectionTemplateIds
        );
        _choicePedestalSnapshot = CreateChoicePedestalSnapshot(choice);
        _choicePedestalFrame = frame;
        return _choicePedestalSnapshot;
    }

    public EncounterTargetingSnapshot GetTargetingState()
    {
        var frame = Time.frameCount;
        if (_targetingFrame == frame)
            return _targetingSnapshot;

        _targetingSnapshot = ReadTargetingState();
        _targetingFrame = frame;
        return _targetingSnapshot;
    }

    private static EncounterIdsSnapshot ReadEncounterIds()
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
                return new EncounterIdsSnapshot
                {
                    CurrentEncounterId = currentEncounterId,
                    CurrentEncounterTemplateId = currentEncounterTemplateId,
                    IsChoiceState = isChoiceState,
                    IsSelectionState = isSelectionState,
                    ChoiceSelectionEntryIds = Array.Empty<string>(),
                    ChoiceSelectionTemplateIds = Array.Empty<Guid>(),
                };
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

            return new EncounterIdsSnapshot
            {
                CurrentEncounterId = currentEncounterId,
                CurrentEncounterTemplateId = currentEncounterTemplateId,
                IsChoiceState = isChoiceState,
                IsSelectionState = true,
                ChoiceSelectionEntryIds =
                    entryIds.Count == 0 ? Array.Empty<string>() : entryIds.ToArray(),
                ChoiceSelectionTemplateIds =
                    templateIds.Count == 0 ? Array.Empty<Guid>() : templateIds.ToArray(),
            };
        }
        catch (Exception ex)
        {
            BppLog.Error("Encounter", "ReadEncounterIds failed", ex);
            return EncounterIdsSnapshot.Empty;
        }
    }

    private static EncounterTargetingSnapshot ReadTargetingState()
    {
        try
        {
            var appState = AppState.CurrentState;
            var filter = InteractionFilterProbe.ReadCurrentFilter();
            var isPedestalState = appState is PedestalState;
            var pedestalEligible = appState is PedestalState ped
                ? PedestalEligibilityProbe.ReadEligibleInstanceIds(ped)
                : new HashSet<string>();

            return new EncounterTargetingSnapshot
            {
                InteractionFilterTemplateIds = filter,
                PedestalEligibleInstanceIds = pedestalEligible,
                IsPedestalState = isPedestalState,
            };
        }
        catch (Exception ex)
        {
            BppLog.Error("Encounter", "ReadTargetingState failed", ex);
            return EncounterTargetingSnapshot.Empty;
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
