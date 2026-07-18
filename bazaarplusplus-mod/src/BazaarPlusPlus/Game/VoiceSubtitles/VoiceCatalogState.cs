#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal enum VoiceCatalogState
{
    Loading,
    Ready,
    Degraded,
    Failed,
}

internal readonly record struct VoiceCatalogDegradation(
    VoiceCatalogReasonCode ReasonCode,
    VoiceCatalogSource Source
);
