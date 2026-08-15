#nullable enable
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.EventPreview;

/// <summary>
/// Keeps BazaarAgent on the same dynamically evaluated encounter-preview path as the in-game
/// tooltip while presenting its result as plain prompt text rather than TMP markup.
/// </summary>
internal sealed class BazaarAgentEncounterPreview : IBazaarAgentEncounterPreview
{
    private readonly IEncounterPreviewModule _module;

    internal BazaarAgentEncounterPreview(IEncounterPreviewModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public string? ResolveEvent(Guid templateId, string nativeDescription) =>
        Available(_module.ResolveEvent(new EventPreviewQuery(templateId, nativeDescription)));

    public string? ResolveStep(Guid templateId, string nativeDescription) =>
        Available(
            _module.ResolveStep(new EncounterStepPreviewQuery(templateId, nativeDescription))
        );

    private static string? Available(EventPreviewResult result) =>
        result.Availability == EventPreviewAvailability.Available
            ? CardDescriptionTextNormalizer.Normalize(result.Content)
            : null;
}
