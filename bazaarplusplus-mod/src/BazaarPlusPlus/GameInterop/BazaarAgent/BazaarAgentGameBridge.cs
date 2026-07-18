#nullable enable
namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// Cross-plugin handoff point for the BazaarAgent host. BazaarPlusPlus sets
/// <see cref="Current"/> during its own <c>Awake()</c>; the separate BazaarAgent host
/// plugin (which declares <c>[BepInDependency(BazaarPlusPlus)]</c>, so it loads after)
/// reads it. The setter is internal so only BazaarPlusPlus can publish the facade.
/// </summary>
public static class BazaarAgentGameBridge
{
    /// <summary>The live game-interop facade, or <c>null</c> before BazaarPlusPlus has
    /// initialized or after it has been disposed.</summary>
    public static IBazaarAgentGameProbe? Current { get; internal set; }

    /// <summary>The replay video recording facade, or <c>null</c> before BazaarPlusPlus has
    /// initialized or after it has been disposed. Read lazily per call by the host — the
    /// combat-replay runtime behind it is attached after this is published.</summary>
    public static IBazaarAgentReplayRecorder? CurrentRecorder { get; internal set; }
}
