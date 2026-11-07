#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using BazaarPlusPlus.Infrastructure.Logging;
using Spine.Unity;
using UnityEngine;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed class PortraitBreakoutPreparation
{
    private sealed record Cached(
        int Asset,
        string Variant,
        PortraitComposition Fit,
        PortraitFrameGeometry Frame,
        PortraitBreakoutPlan Plan
    );

    private readonly ConditionalWeakTable<Sprite, Cached> _memory = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PortraitBreakoutCache _disk;

    internal PortraitBreakoutPreparation(string directory) =>
        _disk = new PortraitBreakoutCache(directory);

    internal async Task<PortraitBreakoutPlan> PrepareAsync(
        Sprite sprite,
        SkeletonAnimation source,
        string requested,
        PortraitComposition fit,
        PortraitFrameGeometry frame,
        PortraitCapturedImages? images,
        Action? cacheMiss,
        CancellationToken cancellation
    )
    {
        var assetId = source.skeletonDataAsset.GetInstanceID();
        var spriteName = sprite.name;
        var variant =
            requested
            + "|"
            + source.initialSkinName
            + "|"
            + source.initialFlipX
            + "|"
            + source.initialFlipY;
        if (
            _memory.TryGetValue(sprite, out var cached)
            && cached.Asset == assetId
            && cached.Variant == variant
            && cached.Fit == fit
            && cached.Frame == frame
        )
            return cached.Plan;
        // Composition hits can still need this first analysis. Do not leave the game's
        // cached-setup sprite hidden while waiting for the capture/worker queue.
        cacheMiss?.Invoke();
        await _gate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (sprite == null || source == null)
                throw new OperationCanceledException();
            if (
                _memory.TryGetValue(sprite, out cached)
                && cached.Asset == assetId
                && cached.Variant == variant
                && cached.Fit == fit
                && cached.Frame == frame
            )
                return cached.Plan;
            var watch = Stopwatch.StartNew();
            var data = source.skeletonDataAsset.GetSkeletonData(false);
            var animation =
                data.FindAnimation(requested)
                ?? data.FindAnimation("idle")
                ?? data.FindAnimation(source.AnimationName ?? "");
            var sampling =
                animation == null ? null : PortraitBreakoutSampling.Create(animation, fit.PoseTime);
            var plan = PortraitBreakoutPlan.Empty(frame);
            var diskHit = false;
            var reason = sampling == null ? "sampling_limit" : "analyzed";
            if (sampling != null)
            {
                images ??= await NativePortraitPoseCapture.CaptureImagesAsync(
                    sprite,
                    source,
                    cancellation
                );
                var poses = await NativePortraitPoseCapture.CaptureAsync(
                    source,
                    animation!,
                    sampling.Times,
                    cancellation,
                    breakout: true
                );
                if (sampling.StrictSlots.Count != 0)
                    foreach (var pose in poses)
                        for (var i = 0; i < pose.Triangles.Length; i++)
                            if (!sampling.Allows(pose.Triangles[i].Part))
                                pose.Triangles[i] = pose.Triangles[i] with
                                {
                                    BreakoutEligible = false,
                                };
                var input = new PortraitCompositionInput(images.Reference, images.Pages, poses);
                var signature = NativePortraitResourceIdentity.Source(source, requested);
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                budget.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    (plan, diskHit) = await Task.Run(
                        () => _disk.FindOrCompute(input, fit, frame, signature, budget.Token),
                        budget.Token
                    );
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    reason = "budget_exceeded";
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (sprite == null || source == null)
                throw new OperationCanceledException();
            _memory.Remove(sprite);
            _memory.Add(sprite, new Cached(assetId, variant, fit, frame, plan));
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.hero_portrait.breakout"),
                ("sprite", spriteName),
                ("attachments", plan.Parts.Length),
                ("content_scale", plan.ContentScale),
                ("content_offset_y", plan.ContentY),
                (
                    "exterior_pixels",
                    Enumerable.Range(0, plan.Runs.Length / 2).Sum(i => plan.Runs[i * 2 + 1])
                ),
                ("disk_cached", diskHit),
                ("sample_count", sampling?.Times.Length ?? 0),
                ("strict_slots", sampling?.StrictSlots.Count ?? 0),
                ("reason", reason),
                ("elapsed_ms", watch.ElapsedMilliseconds)
            );
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // This refinement is optional: a reliable composition still animates inside
            // the strict frame when capture, cache, or analysis cannot be completed.
            var plan = PortraitBreakoutPlan.Empty(frame);
            if (sprite != null && source != null)
            {
                _memory.Remove(sprite);
                _memory.Add(sprite, new Cached(assetId, variant, fit, frame, plan));
            }
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Plugin,
                    "plugin.hero_portrait.breakout_failed",
                    storm: ["sprite"]
                ),
                ex,
                ("sprite", spriteName)
            );
            return plan;
        }
        finally
        {
            _gate.Release();
        }
    }
}
