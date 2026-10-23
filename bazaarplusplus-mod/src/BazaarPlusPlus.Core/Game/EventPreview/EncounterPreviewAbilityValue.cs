#nullable enable
namespace BazaarPlusPlus.Game.EventPreview;

public sealed class EncounterPreviewAbilityValue
{
    public EncounterPreviewAbilityValue(string valueText, string? unit = null)
    {
        ValueText = valueText ?? string.Empty;
        Unit = unit;
    }

    public string ValueText { get; }

    public string? Unit { get; }
}
