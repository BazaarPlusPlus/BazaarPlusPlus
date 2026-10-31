namespace BazaarPlusPlus.Game.Supporters;

/// <summary>
/// The supporters a touch panel credits, in rotation (ADR-0025 decision 4: a host service the
/// Mobile UI reaches through Core). Each host's composition binds it over its own supporter
/// catalog.
/// </summary>
public interface ISupporterRotation
{
    /// <summary>Main thread. Starts the catalog's load if the host has not started it yet.</summary>
    void Warm();

    /// <summary>
    /// The next <paramref name="count"/> supporters of the rotation, or none while the list has
    /// not loaded: the placeholder fallback is never credited.
    /// </summary>
    IReadOnlyList<BPPSupporterSample> Next(int count);
}
