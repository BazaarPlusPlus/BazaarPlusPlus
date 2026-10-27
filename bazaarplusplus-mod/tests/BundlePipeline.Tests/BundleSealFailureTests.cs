using System.Collections.Concurrent;
using System.IO.Compression;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunScreenshot;
using BazaarPlusPlus.TestSupport;
using BepInEx.Logging;
using MessagePack;
using Microsoft.Data.Sqlite;

// Failure and wait behavior observed through the coordinator, durable queue rows, and structured
// logs.
internal static class BundleSealFailureTests
{
    private const string JsonMismatch =
        "Method not found: 'System.String Newtonsoft.Json.Linq.JToken.ToString(Newtonsoft.Json.Formatting)'.";

    internal static async Task RunAsync(string root)
    {
        await EnvironmentFailureParksUntilNextLaunchAsync(Path.Combine(root, "environment"));
        await PoisonRunCannotStarveLaterRunsAsync(Path.Combine(root, "poison"));
        await PendingValidationKeepsOutboxOnEnvironmentFailureAsync(Path.Combine(root, "pending"));
        await PendingValidationRunsOncePerLaunchAsync(Path.Combine(root, "pending-once"));
        await InvalidReplayDegradesWithLogAsync(Path.Combine(root, "replay"));
        await InvalidReplayReasonReachesLogAsync(Path.Combine(root, "replay-reason"));
        await MaintenanceFailureDoesNotBlockSealingAsync(Path.Combine(root, "maintenance"));
        await EnvironmentFailureKeepsOrphanBundleAsync(Path.Combine(root, "orphan"));
        await ReplayWaitDefersScreenshotAndCompositionAsync(Path.Combine(root, "replay-wait"));
        await SizeBudgetTrimDoesNotWaitAsync(Path.Combine(root, "size-budget"));
        await SizeBudgetKeepsSelectedCandidateAsync(Path.Combine(root, "budget-candidate"));
        await OversizedTerminalEventRemainsTerminalAsync(Path.Combine(root, "budget-terminal"));
        await WorkerWakesOnRunCompletionAsync(Path.Combine(root, "worker-wake"));
        await UploadValidationWakesIdleWorkerAsync(Path.Combine(root, "upload-wake"));
    }

    private static async Task UploadValidationWakesIdleWorkerAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("reseal-run", endedMinutesAgo: 0);
        fixture.AddBattle("reseal-run", "reseal-battle");
        SealInputs.SaveReplay(fixture.Replays, "reseal-battle");
        var passes = 0;
        var recreated = 0;
        var committedBeforePublish = true;
        // Subscribe before the coordinator, so its worker cannot consume the new job before
        // this observer checks that the upload transaction is already visible.
        using var observer = fixture.Services.EventBus.Subscribe<BundleSealJobRecreated>(change =>
        {
            recreated++;
            var previouslyCommitted = committedBeforePublish;
            // Event handlers isolate exceptions. Leave this false if the read throws (for
            // example, if an incorrectly early publication still holds the transaction lock).
            committedBeforePublish = false;
            committedBeforePublish =
                previouslyCommitted
                && change.RunId == "reseal-run"
                && fixture.Queue.ReadJob(change.RunId)?.State == BundleSealJobState.Waiting;
        });
        using var coordinator = DesktopBundleSealCoordinator.Create(
            fixture.Services,
            (stage, _) =>
            {
                if (stage == BundleSealStage.ScreenshotTerminal)
                    Interlocked.Increment(ref passes);
            }
        );
        coordinator.Start();
        await Eventually(() => fixture.OutboxCount("reseal-run") == 1, "The initial Bundle seals.");
        using var session = new BundleUploadFeed.Session(
            fixture.Services,
            180,
            ModApiSession.TryCreate(
                "https://example.test",
                "test",
                null,
                "BundleUpload",
                TimeSpan.FromSeconds(30),
                new RejectUploadHandler()
            )!,
            fixture.Queue,
            new SystemBundleOutboxFiles(PathConstants.BundleOutbox(root))
        );

        foreach (var damage in new[] { "missing", "length", "digest", "paused" })
        {
            await Quiescent(() => Volatile.Read(ref passes));
            var previousFile = fixture.PendingFile("reseal-run");
            if (damage == "paused")
                coordinator.Pause();
            if (damage is "missing" or "paused")
                File.Delete(previousFile);
            else if (damage == "length")
                File.WriteAllBytes(previousFile, [0]);
            else
            {
                var bytes = TestInputs.ScratchBytes(previousFile);
                bytes[^1] ^= 1;
                File.WriteAllBytes(previousFile, bytes);
            }

            var before = recreated;
            await session.RunAttemptAsync(CancellationToken.None);
            Require(
                recreated == before + 1 && committedBeforePublish,
                "Reseal is committed before its wake."
            );
            if (damage == "paused")
            {
                Require(
                    fixture.OutboxCount("reseal-run") == 0,
                    "A paused worker leaves the job pending."
                );
                coordinator.Resume();
            }
            // No manual ReconcileAsync: only the upload event can wake the idle worker before
            // its five-minute safety scan. Eventually's timeout is a liveness bound, not a benchmark.
            await Eventually(
                () => fixture.OutboxCount("reseal-run") == 1,
                "Upload validation must wake the idle worker to reseal."
            );
            var payload = fixture.PendingPayload("reseal-run", out _);
            Require(
                fixture.PendingFile("reseal-run") != previousFile
                    && payload.ReplayableBattleIds.SequenceEqual(["reseal-battle"]),
                "A new valid Bundle replaces the rejected file."
            );
        }

        coordinator.Dispose();
        var stopped = Volatile.Read(ref passes);
        File.Delete(fixture.PendingFile("reseal-run"));
        await session.RunAttemptAsync(CancellationToken.None);
        Require(
            recreated == 5
                && committedBeforePublish
                && Volatile.Read(ref passes) == stopped
                && fixture.Queue.ReadJob("reseal-run")?.State == BundleSealJobState.Waiting,
            "After shutdown, reseal stays durable without invoking the disposed worker."
        );
    }

    private sealed class RejectUploadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "An invalid local Bundle must never reach the network."
            );
    }

    // The running worker discovers a Run when Run Logging reports it completed, and with nothing
    // waiting it stays asleep: no pass on a five-second timer.
    private static async Task WorkerWakesOnRunCompletionAsync(string root)
    {
        using var fixture = new Fixture(root);
        var discoveries = 0;
        var passes = 0;
        var failNextDiscovery = 0;
        var discoveredAt = new ConcurrentQueue<DateTime>();
        void Count(BundleSealStage stage, string subject)
        {
            if (stage == BundleSealStage.JobDiscovery)
            {
                Interlocked.Increment(ref discoveries);
                discoveredAt.Enqueue(DateTime.UtcNow);
                if (Interlocked.Exchange(ref failNextDiscovery, 0) == 1)
                    throw new IOException("database is locked");
            }
            else if (stage == BundleSealStage.ScreenshotTerminal)
                Interlocked.Increment(ref passes);
        }
        using var coordinator = DesktopBundleSealCoordinator.Create(fixture.Services, Count);
        coordinator.Start();
        await Eventually(() => Volatile.Read(ref discoveries) == 1, "Start runs one discovery.");
        await Quiescent(() => Volatile.Read(ref passes));

        // The Run is written without a wake; only the completion report brings the worker.
        fixture.AddCompletedRun("completed-run", endedMinutesAgo: 0);
        fixture.Services.EventBus.Publish(new RunLogRunCompleted { RunId = "completed-run" });
        await Eventually(
            () => fixture.OutboxCount("completed-run") == 1,
            "A reported completion seals the Run."
        );
        Require(
            Volatile.Read(ref discoveries) == 2,
            "A reported completion runs exactly one more job discovery."
        );

        await Quiescent(() => Volatile.Read(ref passes));
        var idle = Volatile.Read(ref passes);
        await Task.Delay(TimeSpan.FromSeconds(6));
        Require(
            Volatile.Read(ref passes) == idle && Volatile.Read(ref discoveries) == 2,
            "With nothing waiting the worker runs no pass."
        );

        // A failed discovery is retried at the input-wait cadence: not at once, not at the cap.
        Interlocked.Exchange(ref failNextDiscovery, 1);
        fixture.AddCompletedRun("retried-run", endedMinutesAgo: 0);
        fixture.Services.EventBus.Publish(new RunLogRunCompleted { RunId = "retried-run" });
        await Eventually(
            () => fixture.OutboxCount("retried-run") == 1,
            "The retried discovery seals the Run."
        );
        var times = discoveredAt.ToArray();
        var retryAfter = (times[^1] - times[^2]).TotalSeconds;
        Require(
            Volatile.Read(ref discoveries) == 4 && retryAfter is >= 4 and < 15,
            $"A failed discovery retries once, about 5 s later (after {retryAfter:0.0} s)."
        );
    }

    private static async Task Eventually(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new InvalidOperationException(message);
            await Task.Delay(50);
        }
    }

    // Waits until the counter holds still for a second, so a pass in flight cannot land later.
    private static async Task Quiescent(Func<int> counter)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var last = counter();
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            var current = counter();
            if (current == last)
                return;
            last = current;
        }
        throw new InvalidOperationException("The seal worker never went idle.");
    }

    // A Run waiting on a replay neither encodes its screenshot nor composes again until replay
    // persistence completes or its deadline arrives.
    private static async Task ReplayWaitDefersScreenshotAndCompositionAsync(string root)
    {
        using var fixture = new Fixture(root);
        var compositions = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(BundleSealStage stage, string subject)
        {
            if (stage == BundleSealStage.Composition)
                compositions[subject] = compositions.GetValueOrDefault(subject) + 1;
        }
        var encoder = new CountingEncoder();
        using var coordinator = DesktopBundleSealCoordinator.Create(
            fixture.Services,
            Count,
            encoder
        );

        fixture.AddCompletedRun("wait-run", endedMinutesAgo: 0, screenshotRequested: true);
        fixture.AddScreenshot("wait-run");
        fixture.AddBattle("wait-run", "wait-battle", recordedMinutesAgo: 1);
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(fixture.OutboxCount("wait-run") == 0, "A missing replay holds the Run.");
        Require(
            compositions["wait-run"] == 1
                && encoder.Calls == 0
                && fixture.Queue.ReadJob("wait-run")!.ScreenshotState
                    == BundleScreenshotState.Waiting,
            "A Run that waits on a replay does not encode its screenshot."
        );
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            compositions["wait-run"] == 1 && encoder.Calls == 0,
            "Without replay persistence the waiting Run is not composed again."
        );

        SealInputs.SaveReplay(fixture.Replays, "wait-battle");
        ReplayPersistenceStateTracker.Enqueued("wait-run");
        ReplayPersistenceStateTracker.Completed("wait-run");
        await coordinator.ReconcileAsync(CancellationToken.None);
        var payload = fixture.PendingPayload("wait-run", out var opened);
        Require(
            compositions["wait-run"] == 2 && encoder.Calls == 1,
            "Replay persistence recomposes the Run, which encodes its screenshot once."
        );
        Require(
            opened.Screenshot is { Length: > 0 }
                && payload.ReplayableBattleIds.SequenceEqual(["wait-battle"])
                && payload.Degradation.ReplayOmittedBattleIds.Count == 0,
            "The replay that landed before the deadline seals with the screenshot."
        );

        // No persistence follows this Run's wait, so only its deadline releases it.
        fixture.AddCompletedRun("deadline-run", endedMinutesAgo: 0);
        fixture.AddBattle("deadline-run", "deadline-battle", recordedMinutesAgo: 1);
        await coordinator.ReconcileAsync(CancellationToken.None);
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("deadline-run") == 0 && compositions["deadline-run"] == 1,
            "The waiting Run is skipped while nothing changed."
        );
        SealInputs.ExpireDeadline(fixture.Database, "deadline-run");
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            compositions["deadline-run"] == 2
                && fixture
                    .PendingPayload("deadline-run", out _)
                    .Degradation.ReplayOmittedBattleIds.SequenceEqual(["deadline-battle"]),
            "At its deadline the Run is composed again and seals with the replay omitted."
        );
    }

    // A replay trimmed to fit the size budget is on disk, so it never holds the Run.
    private static async Task SizeBudgetTrimDoesNotWaitAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("budget-run", endedMinutesAgo: 0);
        fixture.AddBattle("budget-run", "budget-large", recordedMinutesAgo: 2);
        fixture.AddBattle("budget-run", "budget-small", recordedMinutesAgo: 1);
        fixture.Replays.Save(
            new PvpReplayPayload
            {
                BattleId = "budget-large",
                SpawnMessageBytes = OversizedSpawn(),
                CombatMessageBytes = ReplayBytes.Combat,
                DespawnMessageBytes = ReplayBytes.Despawn,
            }
        );
        SealInputs.SaveReplay(fixture.Replays, "budget-small");
        using var coordinator = DesktopBundleSealCoordinator.Create(fixture.Services);
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("budget-run") == 1,
            "An oversized Run with every replay on disk seals before its deadline."
        );
        var payload = fixture.PendingPayload("budget-run", out _);
        Require(
            payload.Degradation.Categories.Contains("projection_battle_trimmed")
                && payload.Degradation.ReplayOmittedBattleIds.SequenceEqual(["budget-large"])
                && payload.ReplayableBattleIds.SequenceEqual(["budget-small"]),
            "The size budget trims the oldest replay and keeps the rest."
        );
    }

    // Seeded random text survives gzip at about three quarters of its length, so the Run segment
    // stays over BundleLimitsV6.MaxRunBytes until the size budget trims this replay.
    private static byte[] OversizedSpawn(
        int entropy = BundleLimitsV6.MaxRunBytes * 2,
        int seed = 504
    )
    {
        var random = new byte[entropy];
        new Random(seed).NextBytes(random);
        return MessagePackSerializer.Serialize(
            new NetMessageGameSim(new GameSim()) { MessageId = Convert.ToBase64String(random) },
            MessagePackConfig.Options
        );
    }

    private static async Task SizeBudgetKeepsSelectedCandidateAsync(string root)
    {
        foreach (var omitScreenshot in new[] { false, true })
        {
            using var fixture = new Fixture(Path.Combine(root, omitScreenshot.ToString()));
            fixture.AddCompletedRun(
                "candidate-run",
                endedMinutesAgo: 10,
                screenshotRequested: omitScreenshot
            );
            for (var i = 1; i <= 7; i++)
            {
                var battleId = "candidate-" + i;
                fixture.AddBattle("candidate-run", battleId, recordedMinutesAgo: 20 - i);
                fixture.Replays.Save(
                    new PvpReplayPayload
                    {
                        BattleId = battleId,
                        SpawnMessageBytes = OversizedSpawn(600_000, seed: 5650 + i),
                        CombatMessageBytes = ReplayBytes.Combat,
                        DespawnMessageBytes = ReplayBytes.Despawn,
                    }
                );
            }
            using var coordinator = DesktopBundleSealCoordinator.Create(fixture.Services);
            await coordinator.ReconcileAsync(CancellationToken.None);
            var payload = fixture.PendingPayload("candidate-run", out var opened);
            // Probes trim 1, 2, 4, then 3. The last probe is too large: the selected bytes
            // must still represent four trims, including the final screenshot flag.
            Require(
                payload.Degradation.ReplayOmittedBattleIds.SequenceEqual(
                    new[] { "candidate-1", "candidate-2", "candidate-3", "candidate-4" }
                )
                    && payload.ReplayableBattleIds.SequenceEqual(
                        new[] { "candidate-5", "candidate-6", "candidate-7" }
                    )
                    && opened.Manifest.Run.Projection.Battles.Count == 3
                    && payload.Degradation.ScreenshotOmitted == omitScreenshot
                    && opened.RunPayload.Length < BundleLimitsV6.MaxRunBytes,
                "The selected fitting candidate, projections and final screenshot flag must agree."
            );
        }
    }

    private static async Task OversizedTerminalEventRemainsTerminalAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("terminal-run", endedMinutesAgo: 10);
        var random = new byte[BundleLimitsV6.MaxRunBytes * 2];
        new Random(565).NextBytes(random);
        new RunLogStore(new TestPaths(root)).AppendEvent(
            "terminal-run",
            new RunLogEvent
            {
                RunId = "terminal-run",
                Seq = 1,
                Ts = DateTimeOffset.UtcNow.AddMinutes(-10),
                Kind = "run_completed",
                EncounterId = Convert.ToBase64String(random),
            }
        );
        using var coordinator = DesktopBundleSealCoordinator.Create(fixture.Services);
        await coordinator.ReconcileAsync(CancellationToken.None);
        var job = fixture.Queue.ReadJob("terminal-run");
        Require(
            fixture.OutboxCount("terminal-run") == 0
                && job?.State == BundleSealJobState.TerminalFailure
                && job.LastErrorCode == "minimal_run_payload_too_large",
            "A Run that cannot fit without removing a terminal event must remain terminal."
        );
    }

    private static async Task EnvironmentFailureParksUntilNextLaunchAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("env-run", endedMinutesAgo: 10);
        var calls = 0;
        void Broken(BundleSealStage stage, string subject)
        {
            if (stage == BundleSealStage.Build && subject == "env-run")
            {
                calls++;
                throw new InvalidOperationException(
                    "Bundle build failed.",
                    new MissingMethodException(JsonMismatch)
                );
            }
        }

        using (var first = DesktopBundleSealCoordinator.Create(fixture.Services, Broken))
        {
            await first.ReconcileAsync(CancellationToken.None);
            var parked = fixture.Queue.ReadJob("env-run")!;
            Require(
                parked.State == BundleSealJobState.Waiting
                    && parked.LastErrorCode == BundleSealFailurePolicy.EnvironmentBlockedCode
                    && parked.Attempts == 1,
                "An environment failure parks the Run as waiting instead of ending it."
            );
            Require(fixture.OutboxCount("env-run") == 0, "A parked Run publishes nothing.");
            var line = fixture.SingleLine("bundle_pipeline.seal.environment_blocked");
            Require(
                line.Contains(
                    "exception_type=System.InvalidOperationException",
                    StringComparison.Ordinal
                ) && line.Contains("MissingMethodException", StringComparison.Ordinal),
                "The environment log carries the wrapper and the binding failure: " + line
            );
            await first.ReconcileAsync(CancellationToken.None);
            Require(calls == 1, "A parked Run is not retried again in the same launch.");
        }

        await Task.Delay(10);
        using (var second = DesktopBundleSealCoordinator.Create(fixture.Services, Broken))
        {
            await second.ReconcileAsync(CancellationToken.None);
            Require(calls == 2, "The next launch retries a parked Run once.");
            Require(
                fixture.Queue.ReadJob("env-run")!.Attempts == 2,
                "Each launch that stays broken counts one parked launch."
            );
        }

        await Task.Delay(10);
        using var repaired = DesktopBundleSealCoordinator.Create(fixture.Services);
        await repaired.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("env-run") == 1 && fixture.Queue.ReadJob("env-run") == null,
            "A launch with a working runtime seals the parked Run."
        );
    }

    private static async Task PoisonRunCannotStarveLaterRunsAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("poison-run", endedMinutesAgo: 20);
        fixture.AddCompletedRun("healthy-run", endedMinutesAgo: 10);
        var calls = 0;
        using var coordinator = DesktopBundleSealCoordinator.Create(
            fixture.Services,
            (stage, subject) =>
            {
                if (stage == BundleSealStage.Allocation && subject == "poison-run")
                {
                    calls++;
                    throw new InvalidOperationException("poison");
                }
            }
        );

        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("healthy-run") == 1,
            "A later Run seals in the same pass as an earlier failing Run."
        );
        var poisoned = fixture.Queue.ReadJob("poison-run")!;
        Require(
            poisoned.State == BundleSealJobState.Waiting
                && poisoned.LastErrorCode == "seal_allocation_failed"
                && poisoned.Attempts == 1,
            "The failing Run is recorded against itself and keeps waiting."
        );
        Require(
            fixture
                .SingleLine("bundle_pipeline.seal.deferred")
                .Contains("category=seal_allocation_failed", StringComparison.Ordinal),
            "A retried failure is logged."
        );
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(calls == 1, "A failing Run backs off instead of retrying on every pass.");
    }

    private static async Task PendingValidationKeepsOutboxOnEnvironmentFailureAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("pending-run", endedMinutesAgo: 10);
        using (var sealer = DesktopBundleSealCoordinator.Create(fixture.Services))
            await sealer.ReconcileAsync(CancellationToken.None);
        Require(fixture.OutboxCount("pending-run") == 1, "Fixture Run seals.");

        using (
            var broken = DesktopBundleSealCoordinator.Create(
                fixture.Services,
                (stage, _) =>
                {
                    if (stage == BundleSealStage.PendingValidation)
                        throw new MissingMethodException(JsonMismatch);
                }
            )
        )
            await broken.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxStatuses("pending-run").SequenceEqual(["pending"])
                && fixture.Queue.ReadJob("pending-run") == null,
            "A runtime that cannot open Bundles must not invalidate a pending outbox."
        );
        fixture.SingleLine("bundle_pipeline.seal.environment_blocked");

        using (
            var corrupt = DesktopBundleSealCoordinator.Create(
                fixture.Services,
                (stage, _) =>
                {
                    if (stage == BundleSealStage.PendingValidation)
                        throw new InvalidDataException("corrupt");
                }
            )
        )
            await corrupt.ReconcileAsync(CancellationToken.None);
        Require(
            fixture
                .OutboxStatuses("pending-run")
                .OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(["pending", "permanent_failure"]),
            "A corrupt pending file is invalidated and resealed."
        );
        Require(
            fixture
                .SingleLine("bundle_pipeline.seal.degraded")
                .Contains("category=pending_file_invalid", StringComparison.Ordinal),
            "The invalidation is logged."
        );
    }

    private static async Task PendingValidationRunsOncePerLaunchAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("pending-once-run", endedMinutesAgo: 10);
        using (var sealer = DesktopBundleSealCoordinator.Create(fixture.Services))
            await sealer.ReconcileAsync(CancellationToken.None);
        Require(fixture.OutboxCount("pending-once-run") == 1, "Fixture Run seals.");

        var validations = 0;
        void Count(BundleSealStage stage, string _)
        {
            if (stage == BundleSealStage.PendingValidation)
                validations++;
        }

        using (var launch = DesktopBundleSealCoordinator.Create(fixture.Services, Count))
        {
            await launch.ReconcileAsync(CancellationToken.None);
            Require(validations == 1, "The first pass of a launch validates the pending file.");
            await launch.ReconcileAsync(CancellationToken.None);
            Require(validations == 1, "Later passes in the same launch do not re-read it.");
        }

        using (var next = DesktopBundleSealCoordinator.Create(fixture.Services, Count))
            await next.ReconcileAsync(CancellationToken.None);
        Require(validations == 2, "The next launch validates it again.");
    }

    private static async Task InvalidReplayDegradesWithLogAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("replay-run", endedMinutesAgo: 10);
        fixture.AddBattle("replay-run", "battle-not-gzip");
        fixture.AddBattle("replay-run", "battle-bad-pack");
        fixture.AddBattle("replay-run", "battle-truncated");
        var replayRoot = PathConstants.CombatReplays(root);
        Directory.CreateDirectory(replayRoot);
        File.WriteAllBytes(Path.Combine(replayRoot, "battle-not-gzip.payload.mpack.gz"), [1, 2, 3]);
        File.WriteAllBytes(
            Path.Combine(replayRoot, "battle-bad-pack.payload.mpack.gz"),
            Gzip([0xC1])
        );
        File.WriteAllBytes(
            Path.Combine(replayRoot, "battle-truncated.payload.mpack.gz"),
            TruncatedReplay("battle-truncated")
        );
        var replays = new CombatReplayPayloadStore(replayRoot);
        var badPack = replays.LoadDetailed("battle-bad-pack");
        Require(
            badPack.Status == FileBackedPayloadLoadStatus.Invalid && badPack.Exception != null,
            "An invalid replay carries its decode exception to the seal policy."
        );
        var truncated = replays.LoadDetailed("battle-truncated");
        Require(
            truncated.Status == FileBackedPayloadLoadStatus.Invalid
                && BundleSealFailurePolicy.Classify(truncated.Exception)
                    == BundleSealFailureCause.Transient,
            "Truncated MessagePack inside valid gzip decodes to an IO-looking exception: "
                + truncated.Exception
        );

        using var coordinator = DesktopBundleSealCoordinator.Create(fixture.Services);
        await coordinator.ReconcileAsync(CancellationToken.None);
        var file = fixture.PendingFile("replay-run");
        var payload = RunPayloadV6Codec.Decode(
            BundleV6Codec.Open(TestInputs.ScratchBytes(file)).RunPayload
        );
        Require(
            payload.Degradation.ReplayOmittedBattleIds.Contains("battle-not-gzip")
                && payload.Degradation.ReplayOmittedBattleIds.Contains("battle-bad-pack")
                && payload.Degradation.ReplayOmittedBattleIds.Contains("battle-truncated"),
            "Invalid replay data, including a truncated payload, omits those replays at once."
        );
        Require(
            fixture
                .SingleLine("bundle_pipeline.seal.degraded")
                .Contains("category=replay_invalid", StringComparison.Ordinal),
            "An omitted replay is logged instead of silently dropped."
        );
    }

    private static async Task InvalidReplayReasonReachesLogAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("reason-run", endedMinutesAgo: 10);
        fixture.AddBattle("reason-run", "empty-replay");
        var replayRoot = PathConstants.CombatReplays(root);
        Directory.CreateDirectory(replayRoot);
        File.WriteAllBytes(Path.Combine(replayRoot, "empty-replay.payload.mpack.gz"), []);
        using var coordinator = DesktopBundleSealCoordinator.Create(fixture.Services);
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("reason-run") == 1,
            "A codec failure without an exception still degrades and seals the Run."
        );
        var line = fixture.SingleLine("bundle_pipeline.seal.degraded");
        Require(
            line.Contains("category=replay_invalid", StringComparison.Ordinal)
                && line.Contains("payload_empty", StringComparison.Ordinal)
                && line.Contains(nameof(PvpReplayPayload), StringComparison.Ordinal),
            "The original codec reason and payload type must reach the seal diagnostic."
        );
    }

    private static async Task MaintenanceFailureDoesNotBlockSealingAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("maintained-run", endedMinutesAgo: 10);
        fixture.Queue.EnsureEligibleJobs(TimeSpan.FromMinutes(2));
        using var coordinator = DesktopBundleSealCoordinator.Create(
            fixture.Services,
            (stage, _) =>
            {
                if (stage is BundleSealStage.FileRecovery or BundleSealStage.JobDiscovery)
                    throw new IOException("database is locked");
            }
        );
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("maintained-run") == 1,
            "File recovery and job discovery failures do not stop waiting Runs from sealing."
        );
        Require(
            fixture
                .Lines("bundle_pipeline.reconcile.failed")
                .Any(line =>
                    line.Contains("category=file_recovery_failed", StringComparison.Ordinal)
                )
                && fixture
                    .Lines("bundle_pipeline.reconcile.failed")
                    .Any(line =>
                        line.Contains("category=job_discovery_failed", StringComparison.Ordinal)
                    ),
            "Each maintenance failure is logged."
        );
    }

    private static async Task EnvironmentFailureKeepsOrphanBundleAsync(string root)
    {
        using var fixture = new Fixture(root);
        var outbox = PathConstants.BundleOutbox(root);
        Directory.CreateDirectory(outbox);
        var built = BundleV6Codec.Build(
            new BundleBuildInputV6
            {
                BundleId = new UlidV6Generator().Next(),
                CreatedAtMs = 1000,
                RunId = "orphan-run",
                PlayerAccountId = "account-failure",
                RunPayload = RunPayloadV6Codec.Encode(
                    new RunPayloadV6 { RunId = "orphan-run", PlayerAccountId = "account-failure" }
                ),
            }
        );
        var orphan = Path.Combine(outbox, built.Manifest.BundleId + ".bundle");
        File.WriteAllBytes(orphan, built.Bytes);
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-2));

        using (
            var broken = DesktopBundleSealCoordinator.Create(
                fixture.Services,
                (stage, _) =>
                {
                    if (stage == BundleSealStage.OrphanAdoption)
                        throw new MissingMethodException(JsonMismatch);
                }
            )
        )
            await broken.ReconcileAsync(CancellationToken.None);
        Require(
            File.Exists(orphan),
            "A runtime that cannot open an expired orphan Bundle must not delete it."
        );
        fixture.SingleLine("bundle_pipeline.seal.environment_blocked");

        using (
            var corrupt = DesktopBundleSealCoordinator.Create(
                fixture.Services,
                (stage, _) =>
                {
                    if (stage == BundleSealStage.OrphanAdoption)
                        throw new InvalidDataException("corrupt");
                }
            )
        )
            await corrupt.ReconcileAsync(CancellationToken.None);
        Require(!File.Exists(orphan), "An unreadable expired orphan is still reclaimed.");
    }

    // A real replay payload whose gzip stream is valid but whose MessagePack is cut in half.
    private static byte[] TruncatedReplay(string battleId)
    {
        var packed = PvpReplayPayloadCodec.Serialize(
            new PvpReplayPayload
            {
                BattleId = battleId,
                SpawnMessageBytes = new byte[256],
                CombatMessageBytes = new byte[256],
                DespawnMessageBytes = new byte[256],
            }
        );
        using var input = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        input.CopyTo(raw);
        return Gzip(raw.ToArray().Take((int)raw.Length / 2).ToArray());
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    private sealed class CountingEncoder : IBundleScreenshotEncoder
    {
        private readonly BundleScreenshotEncoder _inner = new();

        internal int Calls { get; private set; }

        public Task<BundleScreenshotBuildInputV6?> EncodeAsync(
            string absolutePath,
            long capturedAtMs,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return _inner.EncodeAsync(absolutePath, capturedAtMs, cancellationToken);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly RunLogStore _runs;
        private readonly string _database;
        private readonly ManualLogSource _logger = new("bundle-seal-failure-tests");
        private readonly List<string> _output = [];

        internal Fixture(string root)
        {
            Directory.CreateDirectory(root);
            var paths = new TestPaths(root);
            Root = root;
            _runs = new RunLogStore(paths);
            _database = PathConstants.RunLogDatabase(root);
            Queue = new BundleQueueStore(_database);
            Services = new TestServices(paths);
            _logger.LogEvent += (_, args) => _output.Add(args.Data?.ToString() ?? string.Empty);
            BppLog.Install(new BepInExLogSink(_logger));
        }

        internal string Root { get; }
        internal BundleQueueStore Queue { get; }
        internal TestServices Services { get; }

        internal void AddCompletedRun(
            string runId,
            int endedMinutesAgo,
            bool screenshotRequested = false
        )
        {
            var ended = DateTimeOffset.UtcNow.AddMinutes(-endedMinutesAgo);
            _runs.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = runId,
                    StartedAtUtc = ended.AddMinutes(-5),
                    Hero = "Vanessa",
                    GameMode = "Ranked",
                    PlayerAccountId = "account-failure",
                    BundleScreenshotRequested = screenshotRequested,
                    ModVersion = "5.7.0",
                }
            );
            _runs.CompleteRun(
                runId,
                new RunLogCompletion { EndedAtUtc = ended, Status = "completed" }
            );
        }

        internal void AddBattle(string runId, string battleId, int recordedMinutesAgo = 12) =>
            new PvpBattleCatalog(new PvpBattleSqliteStore(_database)).Save(
                new PvpBattleManifest
                {
                    BattleId = battleId,
                    RunId = runId,
                    RecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-recordedMinutesAgo),
                    CombatKind = "PVPCombat",
                    Day = 1,
                    Hour = 1,
                    Participants = new PvpBattleParticipants
                    {
                        PlayerName = "Player",
                        PlayerAccountId = "account-failure",
                        PlayerHero = "Vanessa",
                        OpponentName = "Opponent",
                        OpponentAccountId = "account-opponent",
                        OpponentHero = "Pygmalien",
                    },
                    Outcome = new PvpBattleOutcome
                    {
                        Result = "Win",
                        WinnerCombatantId = "Player",
                        LoserCombatantId = "Opponent",
                    },
                    Snapshots = new PvpBattleSnapshots
                    {
                        PlayerHand = Empty(),
                        PlayerSkills = Empty(),
                        OpponentHand = Empty(),
                        OpponentSkills = Empty(),
                    },
                }
            );

        internal string Database => _database;

        internal CombatReplayPayloadStore Replays => new(PathConstants.CombatReplays(Root));

        internal void AddScreenshot(string runId)
        {
            var relative = $"2099-01-01/final_run-{runId}.png";
            SealInputs.WritePng(Path.Combine(PathConstants.Screenshots(Root), relative));
            new RunScreenshotSqliteStore(_database).Save(
                SealInputs.Screenshot("shot-" + runId, runId, relative, DateTimeOffset.UtcNow)
            );
        }

        internal RunPayloadV6 PendingPayload(string runId, out OpenedBundleV6 opened)
        {
            opened = BundleV6Codec.Open(TestInputs.ScratchBytes(PendingFile(runId)));
            return RunPayloadV6Codec.Decode(opened.RunPayload);
        }

        internal int OutboxCount(string runId) =>
            OutboxStatuses(runId).Count(status => status == "pending");

        internal IReadOnlyList<string> OutboxStatuses(string runId) =>
            Query("SELECT status FROM bundle_outbox WHERE run_id=$runId;", runId);

        internal string PendingFile(string runId) =>
            Path.Combine(
                PathConstants.BundleOutbox(Root),
                Query(
                        "SELECT file_name FROM bundle_outbox WHERE run_id=$runId AND status='pending';",
                        runId
                    )
                    .Single()
            );

        internal IReadOnlyList<string> Lines(string eventId) =>
            _output
                .Where(line => line.Contains(" event=" + eventId + " ", StringComparison.Ordinal))
                .ToArray();

        internal string SingleLine(string eventId)
        {
            var lines = Lines(eventId);
            Require(
                lines.Count == 1,
                $"Expected one {eventId} line:\n" + string.Join("\n", _output)
            );
            return lines[0];
        }

        public void Dispose()
        {
            _logger.Dispose();
            SqliteConnection.ClearAllPools();
        }

        private IReadOnlyList<string> Query(string sql, string runId)
        {
            using var connection = new SqliteConnection($"Data Source={_database}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$runId", runId);
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
                values.Add(reader.GetString(0));
            return values;
        }

        private static PvpBattleCardSetCapture Empty() =>
            new() { Status = PvpBattleCaptureStatus.CapturedEmpty };
    }
}
