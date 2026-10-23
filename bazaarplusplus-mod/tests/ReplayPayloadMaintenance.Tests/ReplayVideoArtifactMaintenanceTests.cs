#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayVideoArtifactMaintenanceTests
{
    internal static void Run()
    {
        ReconcilePreservesSuccessfulMp4AndCleansOnlyExpiredUnprotectedTemps();
        StaleRecordingFinalMp4SurvivesTheCrashWindow();
        ReconcileUsesSnapshotCasAcrossAConcurrentFinish();
        TenThousandArtifactsUseBulkLinearReconciliation();
        MaintenanceDoesNotFollowSymlinkEscapes();
        RelativePathConversionRejectsPrefixSiblings();
    }

    private static void StaleRecordingFinalMp4SurvivesTheCrashWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-crash-{Guid.NewGuid():N}");
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var catalog = new FakeCatalog([
            Artifact(
                "crashed",
                "crashed.mp4",
                ReplayVideoAttachmentState.Attached,
                status: "RECORDING"
            ),
        ]);
        Directory.CreateDirectory(root);
        try
        {
            var path = CreateFile(root, "crashed.mp4", now.AddDays(-2), 500);

            var result = new ReplayVideoArtifactMaintenanceService(catalog, root).Run(
                now,
                TimeSpan.FromDays(1),
                CancellationToken.None
            );

            Equal(0, result.TempCandidateCount, "final MP4 is never inferred to be failed residue");
            True(File.Exists(path), "recording final MP4 survives the SaveFinish crash window");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ReconcileUsesSnapshotCasAcrossAConcurrentFinish()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-race-{Guid.NewGuid():N}");
        var catalog = new FakeCatalog([
            Artifact("race", "race.mp4", ReplayVideoAttachmentState.Attached, status: "RECORDING"),
        ]);
        catalog.BeforeReconcile = () =>
        {
            catalog.Artifacts[0] = catalog.Artifacts[0] with
            {
                Status = "COMPLETED",
                FileState = ReplayVideoFileState.Present,
                EndedAtUtc = DateTimeOffset.UtcNow,
            };
        };

        _ = new ReplayVideoArtifactMaintenanceService(catalog, root).Run(
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1),
            CancellationToken.None
        );

        Equal(
            ReplayVideoFileState.Present,
            catalog.Artifacts.Single().FileState,
            "stale missing observation cannot overwrite concurrent finish"
        );
    }

    private static void TenThousandArtifactsUseBulkLinearReconciliation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-linear-{Guid.NewGuid():N}");
        var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
        var artifacts = Enumerable
            .Range(0, 10_000)
            .Select(index =>
                Artifact(
                    $"video-{index:D5}",
                    $"video-{index:D5}.mp4",
                    ReplayVideoAttachmentState.Attached
                )
            )
            .ToList();
        Directory.CreateDirectory(root);
        try
        {
            foreach (var artifact in artifacts)
                CreateFile(root, artifact.VideoRelativePath, now, 100);
            var catalog = new FakeCatalog(artifacts);

            var result = new ReplayVideoArtifactMaintenanceService(catalog, root).Run(
                now,
                TimeSpan.FromDays(1),
                CancellationToken.None
            );

            Equal(1, catalog.ListArtifactsCallCount, "video metadata query count");
            Equal(2, catalog.ReconcileCallCount, "bulk reconcile command count");
            Equal(30_000, result.WorkUnits, "linear metadata/file work units");
            Equal(10_000, result.PresentMetadataCount, "large present metadata count");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RelativePathConversionRejectsPrefixSiblings()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"bpp-video-relative-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "recordings");
        var sibling = Path.Combine(parent, "recordings-evil", "outside.mp4");
        True(
            !ReplayVideoManagedPath.TryMakeRelative(root, sibling, out _),
            "prefix sibling path rejected"
        );
        True(
            ReplayVideoManagedPath.TryMakeRelative(
                root,
                Path.Combine(root, "2026", "inside.mp4"),
                out var relative
            )
                && relative == Path.Combine("2026", "inside.mp4"),
            "managed child converted to relative path"
        );
    }

    private static void ReconcilePreservesSuccessfulMp4AndCleansOnlyExpiredUnprotectedTemps()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-video-maint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
            var catalog = new FakeCatalog([
                Artifact("attached", "kept.mp4", ReplayVideoAttachmentState.Attached),
                Artifact("detached", "gone.mp4", ReplayVideoAttachmentState.Detached),
            ]);
            CreateFile(root, "kept.mp4", now.AddDays(-100), 100);
            CreateFile(root, "unknown-success.mp4", now.AddDays(-100), 200);
            var expiredTemp = CreateFile(root, "expired.recording.mp4", now.AddDays(-2), 20);
            var activeTemp = CreateFile(root, "active.recording.mp4", now.AddDays(-2), 20);
            var recentTemp = CreateFile(root, "recent.wav", now.AddMinutes(-10), 20);
            using var protectedLease = ReplayVideoInFlightArtifacts.Protect(
                "active-recording",
                [activeTemp]
            );

            var result = new ReplayVideoArtifactMaintenanceService(
                catalog,
                root,
                ReplayVideoInFlightArtifacts.SnapshotPaths
            ).Run(now, TimeSpan.FromDays(1), CancellationToken.None);

            Equal(1, result.PresentMetadataCount, "present metadata");
            Equal(1, result.MissingMetadataCount, "missing metadata");
            Equal(1, result.UnknownSuccessfulMp4Count, "unknown successful mp4");
            Equal(1, result.TempDeletedCount, "expired temp delete");
            Equal(0, result.DeleteFailureCount, "delete failures");
            True(File.Exists(Path.Combine(root, "kept.mp4")), "known successful MP4 retained");
            True(
                File.Exists(Path.Combine(root, "unknown-success.mp4")),
                "unknown successful MP4 retained"
            );
            True(!File.Exists(expiredTemp), "expired temp deleted");
            True(File.Exists(activeTemp), "in-flight temp retained across TTL");
            True(File.Exists(recentTemp), "temp within TTL retained");
            True(
                catalog.Artifacts.Single(artifact => artifact.VideoId == "detached").FileState
                    == ReplayVideoFileState.Missing,
                "reconcile changes file state without reattaching"
            );

            CreateFile(root, "gone.mp4", now, 300);
            var second = new ReplayVideoArtifactMaintenanceService(
                catalog,
                root,
                ReplayVideoInFlightArtifacts.SnapshotPaths
            ).Run(now.AddMinutes(1), TimeSpan.FromDays(1), CancellationToken.None);
            Equal(0, second.TempDeletedCount, "idempotent second temp cleanup");
            var recovered = catalog.Artifacts.Single(artifact => artifact.VideoId == "detached");
            Equal(ReplayVideoFileState.Present, recovered.FileState, "recovered file state");
            Equal(
                ReplayVideoAttachmentState.Detached,
                recovered.AttachmentState,
                "file recovery does not reattach artifact"
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void MaintenanceDoesNotFollowSymlinkEscapes()
    {
        if (OperatingSystem.IsWindows())
            return;
        var parent = Path.Combine(Path.GetTempPath(), $"bpp-video-maintenance-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "recordings");
        var sibling = Path.Combine(parent, "recordings-evil");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        try
        {
            var outside = Path.Combine(sibling, "outside.wav");
            File.WriteAllText(outside, "outside audio");
            File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-3));
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), sibling);
            var maintenance = new ReplayVideoArtifactMaintenanceService(
                new FakeCatalog([]),
                root
            ).Run(DateTimeOffset.UtcNow, TimeSpan.FromDays(1), CancellationToken.None);
            Equal(0, maintenance.TempDeletedCount, "linked outside temp retained");
            True(File.Exists(outside), "directory enumeration does not follow symlink escapes");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static ReplayVideoArtifactRecord Artifact(
        string id,
        string path,
        ReplayVideoAttachmentState attachment,
        string status = "COMPLETED"
    ) =>
        new(
            id,
            "battle",
            path,
            status,
            attachment,
            ReplayVideoFileState.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            attachment == ReplayVideoAttachmentState.Detached ? DateTimeOffset.UtcNow : null,
            null,
            null
        );

    private static string CreateFile(
        string root,
        string relative,
        DateTimeOffset modified,
        int size
    )
    {
        var path = Path.Combine(root, relative);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        return path;
    }

    private static void True(bool value, string label)
    {
        if (!value)
            throw new InvalidOperationException($"Expected {label}.");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }

    private sealed class FakeCatalog(List<ReplayVideoArtifactRecord> artifacts)
        : IReplayVideoArtifactCatalog
    {
        internal List<ReplayVideoArtifactRecord> Artifacts { get; } = artifacts;
        internal Action? BeforeReconcile { get; set; }
        internal int ListArtifactsCallCount { get; private set; }
        internal int ReconcileCallCount { get; private set; }

        public IReadOnlyList<ReplayVideoArtifactRecord> ListArtifacts()
        {
            ListArtifactsCallCount++;
            return Artifacts.ToList();
        }

        public void ReconcileFileState(
            IReadOnlyCollection<ReplayVideoArtifactRecord> observations,
            ReplayVideoFileState fileState,
            DateTimeOffset reconciledAt
        )
        {
            ReconcileCallCount++;
            var beforeReconcile = BeforeReconcile;
            BeforeReconcile = null;
            beforeReconcile?.Invoke();
            var observed = observations.ToDictionary(
                observation => observation.VideoId,
                StringComparer.Ordinal
            );
            for (var i = 0; i < Artifacts.Count; i++)
            {
                var current = Artifacts[i];
                if (
                    observed.TryGetValue(current.VideoId, out var observation)
                    && current.FileState == observation.FileState
                    && string.Equals(current.Status, observation.Status, StringComparison.Ordinal)
                    && current.EndedAtUtc == observation.EndedAtUtc
                )
                    Artifacts[i] = current with { FileState = fileState };
            }
        }
    }
}
