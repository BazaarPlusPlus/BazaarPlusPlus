using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Warmup;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar;
using TheBazaar.AppFramework;

var template = Guid.Parse("bcab504b-1743-4d20-acd6-16d73da6b8c7");
var card = new PvpBattleCardSnapshot
{
    TemplateId = template.ToString(),
    Type = CardType.Item,
    Size = CardSize.Small,
};
var manifest = new PvpBattleManifest();
manifest.Snapshots.PlayerHand.Status = PvpBattleCaptureStatus.Captured;
manifest.Snapshots.OpponentHand.Status = PvpBattleCaptureStatus.Captured;
manifest.Snapshots.PlayerHand.Items.Add(card);
manifest.Snapshots.OpponentHand.Items.Add(card.Clone());

// A duplicate on the other board shares one preload; a different size is a separate request.
var larger = card.Clone();
larger.Size = CardSize.Large;
manifest.Snapshots.OpponentHand.Items.Add(larger);
var sequence = new CombatSequenceMessages();
sequence.CombatMessage.Data.VfxKeys = ["override", "override", " "];
var outcome = new Outcome();
var loader = new AssetLoader();
Services.Loader = loader;

await Warm();
Assert(
    loader.Cards.Count == 2 && loader.CardCalls == 2,
    "One request per template/size in the initial replay."
);
Assert(loader.SharedLoaded && loader.SharedCalls == 1, "Initial shared preload.");
Assert(
    NativeGlobalAssetLoader.OverrideCalls == 1,
    "Duplicate and blank override keys do not add loads."
);
loader.ReleaseScope();
Assert(
    loader.Cards.Count == 0 && !loader.SharedLoaded,
    "Native scope release removes the resources."
);
await Warm();
Assert(
    loader.Cards.Count == 2 && loader.CardCalls == 4,
    "Second replay must repopulate released card resources."
);
Assert(
    loader.SharedLoaded && loader.SharedCalls == 2,
    "Second replay must delegate shared preload to the game again."
);

loader.ReleaseScope();
loader.FailNextCard = true;
loader.FailNextShared = true;
await Warm();
Assert(
    outcome.Failures == 2 && CombatVfxWarmer.LastStats?.CardsFailed == 1,
    "Failures remain observable without aborting the remaining warmup."
);
await Warm();
Assert(
    loader.Cards.Count == 2 && loader.SharedCalls == 4 && !loader.SharedLoaded,
    "Card retries recover; the game still owns its early shared-preload flag."
);

loader.ReleaseScope();
await Warm();
Assert(loader.SharedLoaded, "Scope release resets the native shared-preload flag.");

loader.CanPreload = false;
var sharedCalls = loader.SharedCalls;
NativeGlobalAssetLoader.ReturnNull = true;
await Warm();
Assert(
    loader.SharedCalls == sharedCalls && CombatVfxWarmer.LastStats?.SharedAssetsSkipped == 1,
    "Unsafe native shared preload remains guarded."
);
Assert(
    CombatVfxWarmer.LastStats is { OverrideAssetsFailed: 1, OverrideAssetsPreloaded: 0 },
    "Null override assets are failures, never successful loads."
);
NativeGlobalAssetLoader.ReturnNull = false;
await Warm();
Assert(
    CombatVfxWarmer.LastStats is { OverrideAssetsFailed: 0, OverrideAssetsPreloaded: 1 },
    "A null override load can be retried."
);

// VFXManager.TryInstantiateVFXByAddress prefers the key's /Emitters/ twin when that address
// exists and falls back to the key only when the twin loads as null.
const string projectile = "VFX/Projectiles/Bolt";
const string emitter = "VFX/Emitters/Bolt";
const string missingTwin = "VFX/Projectiles/Spark";
const string plain = "VFX/Impacts/Hit";
loader.Addresses.Add(emitter);
loader.Addresses.Add(plain);

await WarmOverrides(projectile, projectile, missingTwin, plain);
Assert(
    Loaded() == "VFX/Emitters/Bolt|VFX/Impacts/Hit|VFX/Projectiles/Spark",
    "An existing twin replaces its key, a missing twin and an unreplaced key load the key itself, "
        + "and a duplicate key loads once."
);
Assert(
    NativeGlobalAssetLoader.Loads.All(load => load.Scope == NativeAssetScopeIntent.Current),
    "Override and twin warmup use the scope VFXManager instantiates in."
);
Assert(loader.AddressProbes == 2, "Only a key with a distinct twin probes for the twin.");
Assert(
    CombatVfxWarmer.LastStats is { OverrideAssetsPreloaded: 3, OverrideAssetsFailed: 0 },
    "Each distinct key counts once."
);

NativeGlobalAssetLoader.NullKeys.Add(emitter);
await WarmOverrides(projectile);
Assert(
    Loaded() == "VFX/Emitters/Bolt|VFX/Projectiles/Bolt",
    "A twin that loads as null falls back to warming the key."
);
NativeGlobalAssetLoader.NullKeys.Clear();

var failures = outcome.Failures;
NativeGlobalAssetLoader.ThrowingKeys.Add(emitter);
await WarmOverrides(projectile);
Assert(
    Loaded() == "VFX/Emitters/Bolt|VFX/Projectiles/Bolt"
        && CombatVfxWarmer.LastStats is { OverrideAssetsPreloaded: 1, OverrideAssetsFailed: 0 }
        && outcome.Failures == failures,
    "A throwing twin leaves the key usable without degrading the replay."
);
NativeGlobalAssetLoader.ThrowingKeys.Clear();

Console.WriteLine("Replay presentation warmup lifecycle tests passed.");

async Task WarmOverrides(params string[] keys)
{
    sequence.CombatMessage.Data.VfxKeys = keys;
    loader.ReleaseScope();
    NativeGlobalAssetLoader.Loads.Clear();
    loader.ResetAddressProbes();
    await Warm();
}

string Loaded() =>
    string.Join(
        "|",
        NativeGlobalAssetLoader.Loads.Select(load => load.Key).Order(StringComparer.Ordinal)
    );

Task Warm() => PresentationWarmer.WarmPresentationAssetsAsync(manifest, sequence, outcome);
static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class Outcome : IReplayPlaybackOutcomeSink
{
    public string BattleId => "warmup-lifecycle-test";
    public int Failures { get; private set; }

    public void ReportDegradation(
        ReplayPlaybackReasonCode reasonCode,
        Exception? exception = null
    ) => Failures++;
}
