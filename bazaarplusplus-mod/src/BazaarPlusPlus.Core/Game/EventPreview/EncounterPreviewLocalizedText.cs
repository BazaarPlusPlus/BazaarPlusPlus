#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

public sealed class EncounterPreviewLocalizedText
{
    public EncounterPreviewLocalizedText(string? key, string? fallbackText)
    {
        Key = key;
        FallbackText = fallbackText;
    }

    public string? Key { get; }

    public string? FallbackText { get; }
}
