#nullable enable
using System;

namespace BazaarPlusPlus;

internal static class LocalCardTemplateCatalog
{
    public static bool Contains(Guid templateId)
    {
        return templateId != Guid.Empty
            && EnsureLoaded()
            && CardsJsonCache.TryGetSnapshot(out var snapshot)
            && snapshot.TemplateIds.Contains(templateId);
    }

    internal static bool Warm()
    {
        return CardsJsonCache.Warm();
    }

    internal static void ResetForTests()
    {
        CardsJsonCache.ResetForTests();
    }

    private static bool EnsureLoaded()
    {
        try
        {
            return CardsJsonCache.TryGetSnapshot(out _);
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "LocalCardTemplateCatalog",
                "Failed to load local card template catalog from shared cache",
                ex
            );
            return false;
        }
    }
}
