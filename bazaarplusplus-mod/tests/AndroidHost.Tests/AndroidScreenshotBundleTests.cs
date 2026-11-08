using BazaarPlusPlus.Android;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android end-of-run screenshot as the host stores and seals it, on Core's net10.0 build:
/// the PNG the capture writes (rows read bottom-up, flipped), its <c>run_screenshots</c> row
/// through a DELETE-mode store, and a seal pass with the ImageSharp encoder, so the Bundle
/// carries the JPEG Screenshot segment the BazaarDB feed selects.
/// </summary>
public sealed class AndroidScreenshotBundleTests : IDisposable
{
    private const string RunId = "run-android-shot";
    private const string AccountId = "account-android";
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow.AddMinutes(-40);

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-shot-" + Guid.NewGuid().ToString("N")
    );

    public AndroidScreenshotBundleTests() => Directory.CreateDirectory(_dataRoot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataRoot, recursive: true);
    }

    private string Database => PathConstants.RunLogDatabase(_dataRoot);

    [Fact]
    public void The_capture_writes_an_upright_opaque_png_from_bottom_up_rows()
    {
        // Two rows as a texture holds them: the bottom row (blue, translucent) first.
        byte[] rgba = [0, 0, 255, 10, 0, 0, 255, 10, 255, 0, 0, 255, 255, 0, 0, 255];
        var path = Path.Combine(_dataRoot, "shot.png");

        ScreenshotPngWriter.FlipRowsVertically(rgba, stride: 8, height: 2);
        ScreenshotPngWriter.WriteOpaquePngAtomically(path, rgba, width: 2, height: 2);

        using var image = Image.Load<Rgba32>(path);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[1, 1]);
    }

    [Fact]
    public async Task A_run_that_asked_for_a_screenshot_seals_it_into_its_Bundle()
    {
        var services = new Services(_dataRoot);
        CompletedRankedRun(screenshotRequested: true);
        var capture = await SaveScreenshot(services);
        using var coordinator = Coordinator(services);

        await coordinator.ReconcileAsync(CancellationToken.None);

        var (hasScreenshot, bytes) = Outbox();
        Assert.Equal(1, hasScreenshot);
        var opened = BundleV6Codec.Open(bytes);
        Assert.True(RunBundleV6Contract.Open(bytes).Succeeded);
        var manifest = opened.Manifest.Screenshot;
        Assert.NotNull(manifest);
        Assert.Equal("image/jpeg", manifest!.ContentType);
        Assert.Equal(capture.CapturedAtUtc.ToUnixTimeMilliseconds(), manifest.CapturedAtMs);
        Assert.Equal((64, 30), (manifest.Width, manifest.Height));
        using var jpeg = Image.Load<Rgb24>(opened.Screenshot!);
        Assert.Equal((64, 30), (jpeg.Width, jpeg.Height));
        Assert.False(RunPayloadV6Codec.Decode(opened.RunPayload).Degradation.ScreenshotOmitted);
    }

    [Fact]
    public async Task A_run_that_asked_for_none_seals_Run_only()
    {
        var services = new Services(_dataRoot);
        CompletedRankedRun(screenshotRequested: false);
        await SaveScreenshot(services);
        using var coordinator = Coordinator(services);

        await coordinator.ReconcileAsync(CancellationToken.None);

        var (hasScreenshot, bytes) = Outbox();
        Assert.Equal(0, hasScreenshot);
        Assert.Null(BundleV6Codec.Open(bytes).Manifest.Screenshot);
    }

    private BundleSealCoordinator Coordinator(Services services) =>
        new(
            services,
            new RunPayloadComposer(
                Database,
                PathConstants.CombatReplays(_dataRoot),
                AndroidPvpBattleEvidence.Instance,
                AndroidComposition.DatabaseOptions
            ),
            new BundleScreenshotEncoder(),
            faultProbe: null,
            AndroidComposition.DatabaseOptions
        );

    private async Task<ScreenshotCaptureResult> SaveScreenshot(Services services)
    {
        var capturedAt = Started.AddMinutes(30);
        var relative = ScreenshotPathBuilder.BuildRelativePath(RunId, capturedAt);
        var capture = new ScreenshotCaptureResult
        {
            Metadata = new ScreenshotCaptureMetadata(10, 3, "Vanessa", "Gold", 1200, null),
            ScreenshotId = "shot-android",
            RunId = RunId,
            RelativePath = relative,
            FilePath = Path.Combine(PathConstants.Screenshots(_dataRoot), relative),
            CapturedAtLocal = capturedAt,
            CapturedAtUtc = capturedAt,
        };
        var rgba = new byte[64 * 30 * 4];
        for (var index = 0; index < rgba.Length; index++)
            rgba[index] = (byte)(index * 7);
        ScreenshotPngWriter.WriteOpaquePngAtomically(capture.FilePath, rgba, 64, 30);
        var outcome = await new EndOfRunArtifactPersistence(
            services,
            AndroidComposition.DatabaseOptions
        ).PersistAsync(capture, isPrimary: true);
        Assert.Equal(ScreenshotMetadataPersistenceStatus.Saved, outcome.Status);
        return capture;
    }

    private void CompletedRankedRun(bool screenshotRequested)
    {
        var runs = new RunLogStore(new DataRoot(_dataRoot), AndroidComposition.DatabaseOptions);
        runs.CreateRun(
            new RunLogCreateRequest
            {
                RunId = RunId,
                StartedAtUtc = Started,
                Hero = "Vanessa",
                GameMode = "Ranked",
                ModVersion = "6.0.0",
                PlayerAccountId = AccountId,
                BundleScreenshotRequested = screenshotRequested,
            }
        );
        new PvpBattleSqliteStore(Database, AndroidComposition.DatabaseOptions).Save(
            new PvpBattleRecord
            {
                BattleId = "battle-1",
                RunId = RunId,
                RecordedAtUtc = Started.AddMinutes(5),
                CombatKind = "PVPCombat",
                Day = 1,
                Hour = 6,
                Participants = new PvpBattleParticipants
                {
                    PlayerAccountId = AccountId,
                    OpponentAccountId = "account-opponent",
                },
                Outcome = new PvpBattleOutcome
                {
                    Result = "win",
                    WinnerCombatantId = "Player",
                    LoserCombatantId = "Opponent",
                },
            }
        );
        new CombatReplayPayloadStore(PathConstants.CombatReplays(_dataRoot)).Save(
            ReplayMessages.Payload("battle-1")
        );
        runs.CompleteRun(
            RunId,
            new RunLogCompletion { EndedAtUtc = Started.AddMinutes(30), Status = "completed" }
        );
    }

    private (long HasScreenshot, byte[] Bytes) Outbox()
    {
        using var connection = new SqliteConnection($"Data Source={Database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT has_screenshot, file_name FROM bundle_outbox WHERE run_id=$runId;";
        command.Parameters.AddWithValue("$runId", RunId);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read(), "The Run should seal into the outbox.");
        return (
            reader.GetInt64(0),
            TestInputs.ScratchBytes(
                Path.Combine(PathConstants.BundleOutbox(_dataRoot), reader.GetString(1))
            )
        );
    }

    private sealed class DataRoot(string path) : IPathProvider
    {
        public string? DataRootDirectoryPath => path;
    }

    private sealed class Services(string dataRoot) : IBppServices
    {
        public IBppEventBus EventBus { get; } = new InMemoryBppEventBus();
        public BppSettings Config => null!;
        public IPathProvider Paths { get; } = new DataRoot(dataRoot);
        public IRunContext RunContext => null!;
        public IGameStateProbe GameStateProbe => null!;
        public IEncounterStateProbe EncounterState => null!;
        public IRunSnapshotProbe RunSnapshot => null!;
        public IGameBuildInfo GameBuild { get; } = new Build();
    }

    private sealed class Build : IGameBuildInfo
    {
        public string RawVersion => "test";
        public GameBuildChannel Channel => GameBuildChannel.Online;
    }
}
