#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Paths;
using Xunit;

namespace BazaarPlusPlus.Tests.SavedReplays;

/// <summary>
/// One <see cref="BazaarPlusPlus.Game.CombatReplay.SavedReplayPlayback"/> over an in-memory host
/// and the real publisher, gate, catalog and run lifecycle. Every observable step lands in
/// <see cref="Timeline"/> in the order it happened.
/// </summary>
internal sealed class SavedReplayPlaybackHarness : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-saved-replay-" + Guid.NewGuid().ToString("N")
    );
    private readonly InMemoryBattleCatalog _battles = new();
    private readonly CombatReplayPayloadStore _payloads;

    public SavedReplayPlaybackHarness(bool recordsVideo = true)
    {
        Directory.CreateDirectory(_dataRoot);
        _payloads = new CombatReplayPayloadStore(PathConstants.CombatReplays(_dataRoot));
        Services = new TestServices(_dataRoot);
        Bus.Subscribe<CombatReplayPlaybackStarting>(starting =>
            Timeline.Add($"starting:{starting.BattleId}:record={starting.RecordVideo}")
        );
        Bus.Subscribe<CombatReplayPlaybackEnded>(ended =>
            Timeline.Add($"ended:{ended.BattleId}:{ended.Reason}:failed={ended.Failed}")
        );
        // The bus isolates a throwing handler, so a failed publish fails the publish itself.
        Services.FailPublish = eventData =>
            eventData switch
            {
                CombatReplayPlaybackStarting => StartingPublishFailure,
                CombatReplayPlaybackEnded => EndedPublishFailure,
                _ => null,
            };
        Publisher = new ReplayPlaybackPublisher(Services);
        Host = new InMemorySavedReplayHost(this) { RecordsVideo = recordsVideo };
        Catalog = new SavedReplayCatalog(_battles, _payloads);
        Playback = new BazaarPlusPlus.Game.CombatReplay.SavedReplayPlayback(
            Services,
            new RunLifecycleModule(
                Services.EventBus,
                new FixedGameState(),
                Services.RunContext,
                new SilentRunLifecycleSource()
            ),
            Publisher,
            Gate,
            Catalog,
            Host
        );
        BppLog.Install(new ListLogSink(Log));
    }

    public TestServices Services { get; }
    public InMemoryBppEventBus Bus => Services.Bus;
    public ReplayPlaybackPublisher Publisher { get; }
    public ReplayPayloadOperationGate Gate { get; } = new();
    public SavedReplayCatalog Catalog { get; }
    public InMemorySavedReplayHost Host { get; }
    public BazaarPlusPlus.Game.CombatReplay.SavedReplayPlayback Playback { get; }
    public List<string> Timeline { get; } = [];
    public List<string> Log { get; } = [];
    public Exception? StartingPublishFailure { get; set; }
    public Exception? EndedPublishFailure { get; set; }

    public PvpBattleManifest Save(string battleId)
    {
        var manifest = Manifest(battleId);
        _battles.Save(manifest);
        _payloads.Save(Payload(battleId));
        return manifest;
    }

    /// <summary>The playback lease is free: nothing the module ran still holds it.</summary>
    public bool LeaseIsFree()
    {
        using var lease = Gate.TryAcquirePlayback();
        return lease != null;
    }

    public IReadOnlyList<string> LogLines(string eventName) =>
        Log.Where(line => line.Contains(eventName, StringComparison.Ordinal)).ToList();

    public string SingleTerminal()
    {
        var terminals = Log.Where(IsTerminal).ToList();
        return Assert.Single(terminals);
    }

    public IReadOnlyList<string> Terminals() => Log.Where(IsTerminal).ToList();

    private static bool IsTerminal(string line) =>
        line.Contains("combat_replay.playback.failed", StringComparison.Ordinal)
        || line.Contains("combat_replay.playback.degraded", StringComparison.Ordinal)
        || line.Contains("combat_replay.playback.succeeded", StringComparison.Ordinal);

    public static PvpBattleManifest Manifest(string battleId) =>
        new()
        {
            BattleId = battleId,
            RecordedAtUtc = DateTimeOffset.UnixEpoch,
            CombatKind = "PVPCombat",
        };

    public static PvpReplayPayload Payload(string battleId) =>
        new()
        {
            BattleId = battleId,
            Version = 1,
            SpawnMessageBytes = [1],
            CombatMessageBytes = [2],
            DespawnMessageBytes = [3],
        };

    public void Dispose()
    {
        Playback.Dispose();
        Gate.Dispose();
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (IOException) { }
    }

    internal sealed class TestServices : IBppServices
    {
        public TestServices(string dataRoot)
        {
            Paths = new FixedPaths(dataRoot);
            EventBus = new FailingBus(this);
        }

        public InMemoryBppEventBus Bus { get; } = new();
        public Func<object, Exception?> FailPublish { get; set; } = _ => null;
        public IBppEventBus EventBus { get; }
        public BppSettings Config => null!;
        public IPathProvider Paths { get; }
        public IRunContext RunContext { get; } = new RunContextStore();
        public IGameStateProbe GameStateProbe { get; } = new FixedGameState();
        public IEncounterStateProbe EncounterState => null!;
        public IRunSnapshotProbe RunSnapshot => null!;
        public IGameBuildInfo GameBuild => null!;
    }

    private sealed class FailingBus(TestServices services) : IBppEventBus
    {
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class => services.Bus.Subscribe(handler);

        public void Publish<TEvent>(TEvent eventData)
            where TEvent : class
        {
            services.Bus.Publish(eventData);
            if (services.FailPublish(eventData) is { } failure)
                throw failure;
        }
    }

    private sealed class FixedPaths(string dataRoot) : IPathProvider
    {
        public string? DataRootDirectoryPath => dataRoot;
    }

    private sealed class FixedGameState : IGameStateProbe
    {
        public bool ComputeIsInGameRun() => false;
    }

    private sealed class SilentRunLifecycleSource : IRunLifecycleSource
    {
        public IDisposable Subscribe(Action runStarted, Action runEnded, Action runInterrupted) =>
            new Nothing();

        public BppLogField[] DescribeState() => [];
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class ListLogSink(List<string> lines) : IBppLogSink
    {
        public void Write(BppLogSeverity severity, string message) => lines.Add(message);
    }

    private sealed class InMemoryBattleCatalog : IPvpBattleCatalog
    {
        private readonly Dictionary<string, PvpBattleManifest> _manifests = new(
            StringComparer.Ordinal
        );

        public void Save(PvpBattleManifest manifest) => _manifests[manifest.BattleId] = manifest;

        public void AttachToRun(string battleId, string runId) { }

        public PvpBattleManifest? TryLoad(string battleId) =>
            _manifests.TryGetValue(battleId, out var manifest) ? manifest : null;

        public bool Exists(string battleId) => _manifests.ContainsKey(battleId);

        public IReadOnlyList<PvpBattleManifest> ListByRunId(string runId) => [];

        public IReadOnlyList<ReplayPayloadMaintenanceRecord> ListReplayMaintenanceInventory() => [];

        public IReadOnlyList<string> ScheduleReplayPayloadDeletion(
            IReadOnlyCollection<string> battleIds,
            DateTimeOffset now
        ) => [];

        public void CompleteReplayPayloadDeletion(
            IReadOnlyCollection<string> battleIds,
            DateTimeOffset now
        ) { }

        public void MarkReplayPayloadMissing(
            IReadOnlyCollection<string> battleIds,
            DateTimeOffset now
        ) { }
    }
}

/// <summary>
/// The host port in memory: a counted scope, a settable clock and game state, recorded cleanup
/// and notices, and starts whose every stage can throw or wait on a gate.
/// </summary>
internal sealed class InMemorySavedReplayHost(SavedReplayPlaybackHarness harness) : ISavedReplayHost
{
    private bool _lobbyLoaded;
    private bool _inReplayState;

    public int ScopeDepth { get; private set; }
    public int ScopeEntries { get; private set; }
    public float Now { get; set; } = 1f;
    public int LobbyReads { get; private set; }
    public int ReplayStateReads { get; private set; }

    public bool InReplayState
    {
        get
        {
            ReplayStateReads++;
            return _inReplayState;
        }
        set => _inReplayState = value;
    }

    public bool LobbyLoaded
    {
        get
        {
            LobbyReads++;
            return _lobbyLoaded;
        }
        set => _lobbyLoaded = value;
    }

    public bool RecordsVideo { get; set; } = true;
    public HashSet<string> Offers { get; } = new(StringComparer.Ordinal);
    public List<string> Forgotten { get; } = [];
    public List<SavedReplayNotice> Notices { get; } = [];
    public Exception? BeginStartFailure { get; set; }
    public Exception? MenuReturnFailure { get; set; }
    public int MenuReturns { get; private set; }

    /// <summary>Configures each start the module begins; the last one is <see cref="Start"/>.</summary>
    public Action<InMemorySavedReplayStart>? ConfigureStart { get; set; }
    public InMemorySavedReplayStart? Start { get; private set; }

    public IDisposable EnterHostContext()
    {
        ScopeDepth++;
        ScopeEntries++;
        return new Scope(this);
    }

    public bool CanOfferSavedReplay(string battleId) => Offers.Contains(battleId);

    public void ForgetOffer(string battleId) => Forgotten.Add(battleId);

    public ISavedReplayStart BeginStart(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        harness.Timeline.Add($"begin-start:{manifest.BattleId}");
        if (BeginStartFailure != null)
            throw BeginStartFailure;
        var start = new InMemorySavedReplayStart(this, harness.Timeline, outcome);
        ConfigureStart?.Invoke(start);
        Start = start;
        return start;
    }

    public ReplayPlaybackCleanupStep[] CleanupSteps(
        SavedReplayCleanupPath path,
        string? battleId
    ) =>
        [
            new("first", () => harness.Timeline.Add($"cleanup:{path}:first:{battleId}")),
            new(
                "second",
                () =>
                {
                    harness.Timeline.Add($"cleanup:{path}:second:{battleId}");
                    if (CleanupFailure != null)
                        throw CleanupFailure;
                }
            ),
        ];

    public Exception? CleanupFailure { get; set; }

    public void ReturnToMainMenu()
    {
        MenuReturns++;
        harness.Timeline.Add("return-to-menu");
        if (MenuReturnFailure != null)
            throw MenuReturnFailure;
    }

    public void Notify(in SavedReplayNotice notice)
    {
        Notices.Add(notice);
        harness.Timeline.Add($"notice:{notice.Kind}:{notice.BattleId}");
    }

    private sealed class Scope(InMemorySavedReplayHost host) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            host.ScopeDepth--;
        }
    }
}

/// <summary>
/// One start in memory. A stage with a gate waits until the test completes it; continuations run
/// on the completing thread, so every interleaving is deterministic.
/// </summary>
internal sealed class InMemorySavedReplayStart(
    InMemorySavedReplayHost host,
    List<string> timeline,
    IReplayPlaybackOutcomeSink outcome
) : ISavedReplayStart
{
    public IReplayPlaybackOutcomeSink Outcome => outcome;
    public bool NeedsBootstrap { get; set; }
    public bool BootstrappedFromLobby { get; set; }
    public TaskCompletionSource? BootstrapGate { get; set; }
    public TaskCompletionSource? InjectGate { get; set; }
    public Exception? PrepareFailure { get; set; }
    public Exception? BootstrapFailure { get; set; }
    public Exception? OpponentFailure { get; set; }
    public Exception? InjectFailure { get; set; }
    public ReplayBootstrapRollback Rollback { get; set; } = ReplayBootstrapRollback.Success;
    public Exception? RollbackThrows { get; set; }
    public int Rollbacks { get; private set; }

    /// <summary>The host scope depth at each stage call, keyed by stage.</summary>
    public Dictionary<string, int> DepthAt { get; } = new(StringComparer.Ordinal);

    public void Prepare()
    {
        Record("prepare");
        if (PrepareFailure != null)
            throw PrepareFailure;
    }

    public async Task<bool> BootstrapAsync()
    {
        Record("bootstrap");
        if (BootstrapGate != null)
            await BootstrapGate.Task;
        DepthAt["bootstrap-resumed"] = host.ScopeDepth;
        if (BootstrapFailure != null)
            throw BootstrapFailure;
        return BootstrappedFromLobby;
    }

    public Task PrepareOpponentAsync()
    {
        Record("opponent");
        return OpponentFailure != null ? Task.FromException(OpponentFailure) : Task.CompletedTask;
    }

    public async Task InjectAsync(Func<ReplayPlaybackPublishOutcome> publishStarting)
    {
        Record("inject");
        if (InjectFailure != null)
            throw InjectFailure;
        // The hosts publish the starting signal once the battle is injected, then play it.
        var published = publishStarting();
        if (!published.Succeeded)
            throw new ReplayPlaybackStartException(
                ReplayPlaybackReasonCode.StartingPublishFailed,
                published.Exception
            );
        if (InjectGate != null)
            await InjectGate.Task;
    }

    public Task<ReplayBootstrapRollback> RollbackBootstrapAsync()
    {
        Rollbacks++;
        Record("rollback");
        if (RollbackThrows != null)
            throw RollbackThrows;
        return Task.FromResult(Rollback);
    }

    private void Record(string stage)
    {
        DepthAt[stage] = host.ScopeDepth;
        timeline.Add("start:" + stage);
    }
}
