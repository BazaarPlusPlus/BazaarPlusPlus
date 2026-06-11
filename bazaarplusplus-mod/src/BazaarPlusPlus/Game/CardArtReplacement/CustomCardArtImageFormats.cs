#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.CardArtReplacement;

/// <summary>
/// Custom card art is shipped and loaded as baseline JPEG only, so Unity's
/// <c>Texture2D.LoadImage</c> decodes it reliably. Discovery is pinned to the
/// <c>.jpg</c> extension. Single source of truth shared by the on-disk catalog
/// and the bundled-resource installer so the two never drift.
/// </summary>
internal static class CustomCardArtImageFormats
{
    public const string Extension = ".jpg";

    public static bool TryGetTemplateId(string fileName, out Guid templateId)
    {
        templateId = Guid.Empty;
        if (!Path.GetExtension(fileName).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        return Guid.TryParse(Path.GetFileNameWithoutExtension(fileName), out templateId)
            && templateId != Guid.Empty;
    }
}
