#nullable enable
namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// Agent-facing projection of BazaarPlusPlus's dynamic encounter preview feature.
/// Returned text is plain prompt text: it contains no TMP rendering markup.
/// </summary>
public interface IBazaarAgentEncounterPreview
{
    /// <summary>Resolves the additional detail for an event encounter, when available.</summary>
    string? ResolveEvent(Guid templateId, string nativeDescription);

    /// <summary>Resolves the additional detail for an encounter-step reward, when available.</summary>
    string? ResolveStep(Guid templateId, string nativeDescription);
}
