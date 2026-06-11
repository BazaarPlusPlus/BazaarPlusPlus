#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Resolves the encounter-card type name from a
/// <c>RunState.CurrentEncounterId</c> GUID. Pure read, main thread only.</summary>
internal static class EncounterTypeResolver
{
    public static string? Resolve(string? currentEncounterId)
    {
        if (string.IsNullOrWhiteSpace(currentEncounterId))
            return null;
        if (!Guid.TryParse(currentEncounterId, out var templateId))
            return null;

        var staticType = ResolveFromStaticData(templateId);
        if (!string.IsNullOrEmpty(staticType))
            return staticType;

        var entities = Data.Entities;
        if (entities == null)
            return null;

        foreach (var entity in entities.Values)
        {
            if (entity is not Card card)
                continue;
            if (card.TemplateId != templateId)
                continue;
            return card.Template?.GetType().Name ?? card.Type.ToString();
        }
        return null;
    }

    private static string? ResolveFromStaticData(Guid templateId)
    {
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
            return template?.GetType().Name ?? template?.Type.ToString();
        }
        catch
        {
            return null;
        }
    }
}
