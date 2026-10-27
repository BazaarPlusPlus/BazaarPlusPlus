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
Console.WriteLine("Replay presentation warmup lifecycle tests passed.");

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
