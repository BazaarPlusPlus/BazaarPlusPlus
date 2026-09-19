#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;

internal static class ReplayPayloadOperationGateTests
{
    internal static void Run()
    {
        MaintenanceCannotEnterBetweenPayloadAndManifestPersistence();
        PlaybackLeaseProtectsThePayloadUntilTerminalRelease();
    }

    private static void PlaybackLeaseProtectsThePayloadUntilTerminalRelease()
    {
        using var operationGate = new ReplayPayloadOperationGate();
        using var maintenanceEntered = new ManualResetEventSlim();
        var playbackLease = operationGate.TryAcquirePlayback();
        Assert(playbackLease != null, "Playback should acquire an idle payload gate.");

        var maintenance = Task.Run(() =>
        {
            using var maintenanceLease = operationGate.AcquireMaintenance(CancellationToken.None);
            maintenanceEntered.Set();
        });
        Assert(
            !maintenanceEntered.Wait(TimeSpan.FromMilliseconds(100)),
            "Maintenance entered while playback still held its payload lease."
        );
        playbackLease!.Dispose();
        Assert(
            maintenanceEntered.Wait(TimeSpan.FromSeconds(5)),
            "Maintenance did not resume after playback terminal release."
        );
        maintenance.GetAwaiter().GetResult();
    }

    private static void MaintenanceCannotEnterBetweenPayloadAndManifestPersistence()
    {
        using var operationGate = new ReplayPayloadOperationGate();
        using var payloadSaved = new ManualResetEventSlim();
        using var allowManifest = new ManualResetEventSlim();
        using var manifestSaved = new ManualResetEventSlim();
        using var maintenanceEntered = new ManualResetEventSlim();
        using var queue = new CombatReplayPersistenceQueue(
            _ =>
            {
                payloadSaved.Set();
                allowManifest.Wait(TimeSpan.FromSeconds(5));
            },
            _ => manifestSaved.Set(),
            _ => { },
            operationGate
        );
        var manifest = new PvpBattleManifest
        {
            BattleId = "barrier-battle",
            CombatKind = "PVPCombat",
        };
        queue.Enqueue(new PvpReplayPayload { BattleId = manifest.BattleId }, manifest);
        Assert(payloadSaved.Wait(TimeSpan.FromSeconds(5)), "Payload save barrier was not reached.");

        var maintenance = Task.Run(() =>
        {
            using var lease = operationGate.AcquireMaintenance(CancellationToken.None);
            maintenanceEntered.Set();
        });
        Assert(
            !maintenanceEntered.Wait(TimeSpan.FromMilliseconds(100)),
            "Maintenance entered between payload and manifest persistence."
        );

        allowManifest.Set();
        Assert(manifestSaved.Wait(TimeSpan.FromSeconds(5)), "Manifest save did not finish.");
        Assert(
            maintenanceEntered.Wait(TimeSpan.FromSeconds(5)),
            "Maintenance did not enter after the atomic persistence interval."
        );
        maintenance.GetAwaiter().GetResult();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
