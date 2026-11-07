#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using BazaarPlusPlus.Infrastructure.Logging;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

// Capture Unity resources on the main thread, then pass detached numeric inputs to a single
// background worker. Weak process caches contain no textures, scenes, or prefab instances.
internal sealed class NativePortraitComposition
{
    private sealed record Cached(
        int SkeletonAsset,
        string Variant,
        string SkinKey,
        PortraitComposition? Fit
    );

    private readonly ConditionalWeakTable<Sprite, Cached> _memory = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PortraitCompositionCache _disk;
    private readonly PortraitResourceCache _resources;

    internal NativePortraitComposition(string directory)
    {
        _disk = new PortraitCompositionCache(directory);
        _resources = new PortraitResourceCache(directory);
    }

    private static string SkinKey(NativeHeroPortraitSkin skin) =>
        skin.CharacterPrefab?.RuntimeKey + "|" + skin.IdleAnimation;

    internal bool HasPreparedCandidate(Sprite sprite, NativeHeroPortraitSkin skin)
    {
        if (_memory.TryGetValue(sprite, out var memory) && memory.SkinKey == SkinKey(skin))
            return memory.Fit != null;
        var key = NativePortraitResourceIdentity.Capture(skin, sprite);
        var indexed = key != null && _resources.TryGet(key, out _);
        var legacy = !indexed && key != null && _resources.CanProbeLegacy(key);
        Report(
            sprite.name,
            indexed ? "indexed_candidate"
                : legacy ? "legacy_candidate"
                : key == null ? "resource_key_unavailable"
                : "not_indexed"
        );
        return indexed || legacy;
    }

    internal async Task<PortraitComposition?> MatchAsync(
        Sprite sprite,
        SkeletonAnimation source,
        NativeHeroPortraitSkin skinInfo,
        string animationName,
        CancellationToken cancellation,
        Action? cacheMiss = null,
        Action<PortraitCapturedImages>? capturedImages = null
    )
    {
        var assetId = source.skeletonDataAsset.GetInstanceID();
        var spriteName = sprite.name;
        var variant =
            $"{animationName}|{source.initialSkinName}|{source.initialFlipX}|{source.initialFlipY}";
        if (
            _memory.TryGetValue(sprite, out var cached)
            && cached.SkeletonAsset == assetId
            && cached.Variant == variant
        )
        {
            if (cached.Fit == null)
                cacheMiss?.Invoke();
            return cached.Fit;
        }
        var resourceKey = NativePortraitResourceIdentity.Capture(skinInfo, sprite);
        var sourceSignature = NativePortraitResourceIdentity.Source(source, animationName);
        if (
            resourceKey != null
            && sourceSignature.Length != 0
            && _resources.TryGet(resourceKey, out var ready)
            && ready.Source == sourceSignature
            && ValidateReady(sprite, source, animationName, ready.Fit)
        )
        {
            cancellation.ThrowIfCancellationRequested();
            _memory.Remove(sprite);
            _memory.Add(sprite, new Cached(assetId, variant, SkinKey(skinInfo), ready.Fit));
            Report(spriteName, "resource_cache_hit", ready.Fit.Inliers);
            return ready.Fit;
        }
        await _gate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (sprite == null || source == null)
                throw new OperationCanceledException();
            if (
                _memory.TryGetValue(sprite, out cached)
                && cached.SkeletonAsset == assetId
                && cached.Variant == variant
            )
            {
                if (cached.Fit == null)
                    cacheMiss?.Invoke();
                return cached.Fit;
            }
            // Packed sprite geometry requires reconstruction; fail safely rather than matching
            // a rotated atlas rectangle as if it were the original portrait canvas.
            if (sprite.packed)
            {
                Report(spriteName, "packed_sprite");
                return null;
            }
            var asset = source.skeletonDataAsset;
            var data = asset.GetSkeletonData(false);
            var animation = data.FindAnimation(animationName) ?? data.FindAnimation("idle");
            if (animation == null && !string.IsNullOrEmpty(source.AnimationName))
                animation = data.FindAnimation(source.AnimationName);
            if (animation == null)
            {
                Report(spriteName, "missing_animation");
                return null;
            }
            var images = await NativePortraitPoseCapture.CaptureImagesAsync(
                sprite,
                source,
                cancellation
            );
            capturedImages?.Invoke(images);
            var poses = await NativePortraitPoseCapture.CaptureAsync(
                source,
                animation,
                Enumerable.Range(0, 16).Select(i => animation.Duration * i / 16).ToArray(),
                cancellation,
                breakout: false
            );
            var input = new PortraitCompositionInput(images.Reference, images.Pages, poses);
            Report(spriteName, "capture_completed", poses.Sum(p => p.Triangles.Length));
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            // Cold searches on the game's Mono runtime can take longer than a typical
            // portrait. Work stays on the single background worker; native art remains
            // visible, and the caller still cancels when the portrait goes away.
            budget.CancelAfter(TimeSpan.FromSeconds(12));
            PortraitComposition? fit;
            var timedOut = false;
            var watch = Stopwatch.StartNew();
            try
            {
                var existing = await Task.Run(
                    () =>
                    {
                        var value = _disk.FindCached(input, budget.Token, out var key);
                        return (Fit: value, Key: key);
                    },
                    budget.Token
                );
                fit = existing.Fit;
                Report(
                    spriteName,
                    fit == null ? "disk_cache_miss" : "disk_cache_hit",
                    fit?.Inliers ?? 0
                );
                if (fit == null)
                {
                    if (resourceKey != null)
                        _resources.MarkMiss(resourceKey);
                    // Unity renderer ownership is released on the main thread before
                    // expensive matching starts on an actual cache miss.
                    cacheMiss?.Invoke();
                    fit = await Task.Run(
                        () => _disk.Compute(input, existing.Key, budget.Token),
                        budget.Token
                    );
                }
                if (fit != null && resourceKey != null && sourceSignature.Length != 0)
                {
                    var saved = fit;
                    await Task.Run(
                        () => _resources.Record(resourceKey, sourceSignature, existing.Key, saved),
                        budget.Token
                    );
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                fit = null;
                timedOut = true;
            }
            cancellation.ThrowIfCancellationRequested();
            Report(
                spriteName,
                timedOut ? "budget_exceeded"
                    : fit == null ? "no_reliable_match"
                    : "matched",
                fit?.Inliers ?? 0,
                watch.ElapsedMilliseconds
            );
            // A budget timeout is a failed composition for this loaded sprite too. Keep
            // its static fallback across setups; caller cancellation above remains retryable.
            if (sprite != null && source != null)
            {
                _memory.Remove(sprite);
                _memory.Add(sprite, new Cached(assetId, variant, SkinKey(skinInfo), fit));
            }
            return fit;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ValidateReady(
        Sprite sprite,
        SkeletonAnimation source,
        string requested,
        PortraitComposition fit
    )
    {
        var data = source.skeletonDataAsset.GetSkeletonData(false);
        var animation = data.FindAnimation(requested) ?? data.FindAnimation("idle");
        if (animation == null && !string.IsNullOrEmpty(source.AnimationName))
            animation = data.FindAnimation(source.AnimationName);
        if (animation == null)
            return false;
        var sample =
            animation.Duration > 0 ? (int)MathF.Round(fit.PoseTime / animation.Duration * 16) : 0;
        if (
            sample is < 0 or > 15
            || MathF.Abs(fit.PoseTime - animation.Duration * sample / 16) > .0001f
        )
            return false;
        var skeleton = NativePortraitPoseCapture.CreateSkeleton(data, source);
        var state = new Spine.AnimationState(new AnimationStateData(data));
        state.SetAnimation(0, animation, true).TrackTime = fit.PoseTime;
        state.Apply(skeleton);
        skeleton.UpdateWorldTransform(Skeleton.Physics.None);
        var pages = source
            .skeletonDataAsset.atlasAssets.SelectMany(a => a.GetAtlas().Pages)
            .Distinct()
            .ToList();
        var factor = Math.Min(1f, 256 / Math.Max(sprite.rect.width, sprite.rect.height));
        var reference = new PortraitImage(
            Math.Max(2, (int)MathF.Round(sprite.rect.width * factor)),
            Math.Max(2, (int)MathF.Round(sprite.rect.height * factor)),
            []
        );
        return PortraitCompositionMatcher.IsApplicable(
            new PortraitCompositionInput(
                reference,
                [],
                [
                    new(
                        fit.PoseTime,
                        NativePortraitPoseCapture.CaptureTriangles(skeleton, pages, breakout: false)
                    ),
                ]
            ),
            fit
        );
    }

    private static void Report(string sprite, string reason, int count = 0, long elapsedMs = 0) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Plugin, "plugin.hero_portrait.match_state"),
            ("sprite", sprite),
            ("reason", reason),
            ("count", count),
            ("elapsed_ms", elapsedMs)
        );
}
