#nullable enable

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

internal readonly record struct PortraitPoint(float X, float Y);

internal sealed record PortraitBoneInfluence(float Weight, int[] Ancestors);

internal sealed record PortraitRigBinding(PortraitBoneInfluence[] Influences);

internal sealed record PortraitTriangle(
    int Page,
    PortraitPoint A,
    PortraitPoint B,
    PortraitPoint C,
    PortraitPoint UvA,
    PortraitPoint UvB,
    PortraitPoint UvC,
    string? Part = null,
    float Opacity = 1f,
    bool BreakoutEligible = false,
    PortraitRigBinding? Rig = null
);

internal sealed record PortraitPose(
    float Time,
    PortraitTriangle[] Triangles,
    bool BreakoutSupported = true,
    string[]? DrawOrder = null
);

internal sealed record PortraitCapturedImages(PortraitImage Reference, PortraitImage[] Pages);

internal sealed record PortraitCompositionInput(
    PortraitImage Reference,
    PortraitImage[] Pages,
    PortraitPose[] Poses
);

// Scale and translation map character-local coordinates into reference pixels / image height.
internal sealed record PortraitComposition(
    float Scale,
    float X,
    float Y,
    float PoseTime,
    int Inliers,
    float Error,
    float SpreadX,
    float SpreadY
);

internal static class PortraitCompositionMatcher
{
    private sealed record ImageMatch(
        int Id,
        int Page,
        PortraitPoint Uv,
        PortraitPoint Target,
        float Distance
    );

    private sealed record Pair(int Id, PortraitPoint Source, PortraitPoint Target);

    private const float InlierRadius = 0.018f;

    internal static PortraitComposition? Match(
        PortraitCompositionInput input,
        CancellationToken cancellation
    )
    {
        var target = PortraitImageFeatures.Extract(input.Reference, cancellation);
        if (target.Count < 7)
            return null;
        var matches = MatchImages(input.Pages, input.Reference, target, cancellation);
        var fit = MatchAtlas(input, matches, cancellation);
        return fit ?? MatchAssembled(input, target, cancellation);
    }

    private static PortraitComposition? MatchAtlas(
        PortraitCompositionInput input,
        List<ImageMatch> matches,
        CancellationToken cancellation
    )
    {
        if (matches.Count < 7)
            return null;
        PortraitComposition? best = null;
        foreach (var pose in input.Poses)
        {
            cancellation.ThrowIfCancellationRequested();
            var pairs = new List<Pair>();
            foreach (var match in matches)
            {
                var count = 0;
                foreach (var triangle in pose.Triangles)
                {
                    if (triangle.Page != match.Page || !TryMap(triangle, match.Uv, out var point))
                        continue;
                    if (!pairs.Any(p => p.Id == match.Id && Squared(p.Source, point) < 0.000001f))
                    {
                        pairs.Add(new Pair(match.Id, point, match.Target));
                        if (++count == 6)
                            break;
                    }
                }
            }
            var fit = Fit(pairs, pose.Time, cancellation);
            if (
                fit != null
                && IsApplicable(input, fit)
                && (best == null || Quality(fit) > Quality(best))
            )
                best = fit;
        }
        return best;
    }

    private static PortraitComposition? MatchAssembled(
        PortraitCompositionInput input,
        List<PortraitFeature> target,
        CancellationToken cancellation
    )
    {
        PortraitComposition? best = null;
        var count = Math.Min(4, input.Poses.Length);
        for (var i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var pose = input.Poses[i * input.Poses.Length / count];
            var rendered = PortraitPoseRasterizer.Render(input.Pages, pose, cancellation);
            if (rendered == null)
                continue;
            var matches = MatchImages([rendered.Image], input.Reference, target, cancellation);
            var pairs = matches
                .Select(m => new Pair(m.Id, rendered.ToCharacter(m.Uv), m.Target))
                .ToList();
            var fit = Fit(pairs, pose.Time, cancellation);
            if (fit == null || !IsApplicable(input, fit))
                continue;
            if (best == null || Quality(fit) > Quality(best))
                best = fit;
            // A well-supported match can finish before the remaining proxy poses are built.
            if (
                fit.Inliers >= 14
                && fit.SpreadX >= 0.2f
                && fit.SpreadY >= 0.2f
                && fit.Error <= 0.012f
            )
                return best;
        }
        return best;
    }

    internal static bool IsReliable(PortraitComposition fit) =>
        Finite(fit.Scale)
        && fit.Scale is > 0.000001f and < 1000f
        && Finite(fit.X)
        && MathF.Abs(fit.X) < 100
        && Finite(fit.Y)
        && MathF.Abs(fit.Y) < 100
        && Finite(fit.PoseTime)
        && fit.PoseTime >= 0
        && fit.Inliers >= 7
        && Finite(fit.Error)
        && fit.Error is >= 0f and <= InlierRadius
        && Finite(fit.SpreadX)
        && Finite(fit.SpreadY)
        && fit.SpreadX >= 0.12f
        && fit.SpreadY >= 0.12f;

    internal static bool IsApplicable(PortraitCompositionInput input, PortraitComposition fit)
    {
        if (
            !IsReliable(fit)
            || fit.SpreadX > input.Reference.Width / (float)input.Reference.Height
            || fit.SpreadY > 1f
        )
            return false;
        var pose = input.Poses.FirstOrDefault(p => MathF.Abs(p.Time - fit.PoseTime) < 0.0001f);
        if (pose == null || pose.Triangles.Length == 0)
            return false;
        var low = pose.Triangles.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)));
        var high = pose.Triangles.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
        return fit.Scale * (high - low) is >= 0.75f and <= 8f;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static float Quality(PortraitComposition fit) => fit.Inliers - fit.Error * 30;

    private static List<ImageMatch> MatchImages(
        PortraitImage[] pages,
        PortraitImage referenceImage,
        List<PortraitFeature> target,
        CancellationToken cancellation
    )
    {
        var candidates = new List<ImageMatch>();
        if (target.Count < 2)
            return candidates;
        for (var page = 0; page < pages.Length; page++)
        {
            var image = pages[page];
            var source = PortraitImageFeatures.Extract(image, cancellation);
            foreach (var feature in source)
            {
                cancellation.ThrowIfCancellationRequested();
                var best = float.PositiveInfinity;
                var second = best;
                var index = -1;
                for (var i = 0; i < target.Count; i++)
                {
                    var distance = 0f;
                    var other = target[i].Descriptor;
                    for (var j = 0; j < other.Length; j++)
                    {
                        var delta = feature.Descriptor[j] - other[j];
                        distance += delta * delta;
                        if (distance >= second)
                            break;
                    }
                    if (distance < best)
                    {
                        second = best;
                        best = distance;
                        index = i;
                    }
                    else if (distance < second)
                        second = distance;
                }
                if (index < 0 || best >= second * 0.78f * 0.78f || best > 0.55f)
                    continue;
                var reference = target[index];
                candidates.Add(
                    new ImageMatch(
                        0,
                        page,
                        new PortraitPoint(feature.X / image.Width, feature.Y / image.Height),
                        new PortraitPoint(
                            reference.X / referenceImage.Height,
                            reference.Y / referenceImage.Height
                        ),
                        best
                    )
                );
            }
        }
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var result = new List<ImageMatch>();
        var spacing = 3f / referenceImage.Height;
        foreach (var candidate in candidates)
        {
            // Several scales/orientations of one corner are one independent observation.
            if (result.Any(m => Squared(m.Target, candidate.Target) < spacing * spacing))
                continue;
            result.Add(candidate with { Id = result.Count });
            if (result.Count == 96)
                break;
        }
        return result;
    }

    private static bool TryMap(PortraitTriangle t, PortraitPoint uv, out PortraitPoint point)
    {
        point = default;
        var bx = t.UvB.X - t.UvA.X;
        var by = t.UvB.Y - t.UvA.Y;
        var cx = t.UvC.X - t.UvA.X;
        var cy = t.UvC.Y - t.UvA.Y;
        var denominator = bx * cy - by * cx;
        if (MathF.Abs(denominator) < 1e-10f)
            return false;
        var ux = uv.X - t.UvA.X;
        var uy = uv.Y - t.UvA.Y;
        var b = (ux * cy - uy * cx) / denominator;
        var c = (bx * uy - by * ux) / denominator;
        var a = 1 - b - c;
        if (a < -0.0001f || b < -0.0001f || c < -0.0001f)
            return false;
        point = new PortraitPoint(
            a * t.A.X + b * t.B.X + c * t.C.X,
            a * t.A.Y + b * t.B.Y + c * t.C.Y
        );
        return true;
    }

    private static PortraitComposition? Fit(
        List<Pair> pairs,
        float time,
        CancellationToken cancellation
    )
    {
        if (pairs.Count < 7)
            return null;
        var rng = new Random(731);
        PortraitComposition? best = null;
        var runnerUp = 0;
        for (var attempt = 0; attempt < 768; attempt++)
        {
            if (attempt % 32 == 0)
                cancellation.ThrowIfCancellationRequested();
            var a = pairs[rng.Next(pairs.Count)];
            var b = pairs[rng.Next(pairs.Count)];
            if (a.Id == b.Id || Squared(a.Target, b.Target) < 0.005f)
                continue;
            var dx = a.Source.X - b.Source.X;
            var dy = a.Source.Y - b.Source.Y;
            var d = dx * dx + dy * dy;
            if (d < 0.000001f)
                continue;
            var s = (dx * (a.Target.X - b.Target.X) + dy * (a.Target.Y - b.Target.Y)) / d;
            if (s <= 0 || !Finite(s))
                continue;
            var x = (a.Target.X + b.Target.X - s * (a.Source.X + b.Source.X)) * 0.5f;
            var y = (a.Target.Y + b.Target.Y - s * (a.Source.Y + b.Source.Y)) * 0.5f;
            var inliers = SelectInliers(pairs, s, x, y);
            if (inliers.Count < 7)
                continue;
            // Refine against unique target corners; reused atlas regions cannot inflate support.
            for (var refinement = 0; refinement < 2; refinement++)
            {
                var mx = inliers.Average(p => p.Source.X);
                var my = inliers.Average(p => p.Source.Y);
                var tx = inliers.Average(p => p.Target.X);
                var ty = inliers.Average(p => p.Target.Y);
                var numerator = 0f;
                var denominator = 0f;
                foreach (var p in inliers)
                {
                    var px = p.Source.X - mx;
                    var py = p.Source.Y - my;
                    numerator += px * (p.Target.X - tx) + py * (p.Target.Y - ty);
                    denominator += px * px + py * py;
                }
                if (denominator <= 0)
                    break;
                s = numerator / denominator;
                x = tx - s * mx;
                y = ty - s * my;
                inliers = SelectInliers(pairs, s, x, y);
                if (inliers.Count < 7)
                    break;
            }
            if (inliers.Count < 7)
                continue;
            var error = MathF.Sqrt(inliers.Average(p => Residual(p, s, x, y)));
            var candidate = new PortraitComposition(
                s,
                x,
                y,
                time,
                inliers.Count,
                error,
                inliers.Max(p => p.Target.X) - inliers.Min(p => p.Target.X),
                inliers.Max(p => p.Target.Y) - inliers.Min(p => p.Target.Y)
            );
            if (!IsReliable(candidate))
                continue;
            if (best == null || Quality(candidate) > Quality(best))
            {
                if (best != null && Incompatible(best, candidate))
                    runnerUp = Math.Max(runnerUp, best.Inliers);
                best = candidate;
            }
            else if (Incompatible(best, candidate))
                runnerUp = Math.Max(runnerUp, candidate.Inliers);
        }
        return best != null && runnerUp < best.Inliers * 0.85f ? best : null;
    }

    private static bool Incompatible(PortraitComposition a, PortraitComposition b) =>
        MathF.Abs(MathF.Log(a.Scale / b.Scale)) > 0.15f
        || MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) > 0.2f;

    private static List<Pair> SelectInliers(List<Pair> pairs, float s, float x, float y)
    {
        var selected = new Dictionary<int, (Pair Pair, float Error)>();
        foreach (var pair in pairs)
        {
            var error = Residual(pair, s, x, y);
            if (error > InlierRadius * InlierRadius)
                continue;
            if (!selected.TryGetValue(pair.Id, out var old) || error < old.Error)
                selected[pair.Id] = (pair, error);
        }
        return selected.Values.Select(v => v.Pair).ToList();
    }

    private static float Residual(Pair p, float s, float x, float y)
    {
        var dx = s * p.Source.X + x - p.Target.X;
        var dy = s * p.Source.Y + y - p.Target.Y;
        return dx * dx + dy * dy;
    }

    private static float Squared(PortraitPoint a, PortraitPoint b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
