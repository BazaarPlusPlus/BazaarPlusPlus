#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

// RunInterrupted unloads the player board's SFX banks. Warmup and release run on the Unity main thread.
internal sealed class ReplayAudioBankOwnership
{
    private readonly List<ReplayAudioBank> _owned = [];

    internal bool RecordWarmed(
        string metadataBank,
        string assetBank,
        bool wasMetadataLoaded,
        bool wasAssetLoaded,
        bool isPlayerBoard
    )
    {
        // SoundManager.LoadBankAsync loads nothing when either half is already registered.
        if (isPlayerBoard || wasMetadataLoaded || wasAssetLoaded)
            return false;

        var bank = new ReplayAudioBank(metadataBank, assetBank);
        if (_owned.Any(owned => owned.Matches(bank)))
            return false;

        _owned.Add(bank);
        return true;
    }

    internal IReadOnlyList<ReplayAudioBank> Drain()
    {
        if (_owned.Count == 0)
            return [];

        var drained = _owned.ToList();
        _owned.Clear();
        return drained;
    }
}

internal readonly record struct ReplayAudioBank(string MetadataBank, string AssetBank)
{
    // Either half naming the same bank is the same bank, as SoundManager.LoadBankAsync treats
    // a registered half as the whole bank already loaded.
    internal bool Matches(ReplayAudioBank other) =>
        string.Equals(MetadataBank, other.MetadataBank, StringComparison.Ordinal)
        || string.Equals(AssetBank, other.AssetBank, StringComparison.Ordinal);
}
