#nullable enable

namespace BazaarPlusPlus.Game.HeroPortraits;

internal static class PortraitBreakoutSelection
{
    private sealed record Assembly(int[] Parts, float Value, bool Truncated);

    private sealed record Projection(bool[] Mask, int[] Areas, uint[] Stamp, int[][] PosePixels)
    {
        internal bool Complete = true;
    }

    internal static PortraitBreakoutPlan Select(
        PortraitBreakoutPlan grid,
        PortraitBreakoutPart[] parts,
        int[][] groups,
        PortraitBreakoutCoverage[][] coverage,
        int[][] drawOrders,
        CancellationToken cancellation
    )
    {
        var interior = grid.Interior();
        var framePixels = interior.Count(p => p);
        var assemblies = groups
            .Where(g => g.Any(i => parts[i].Exterior != null))
            .Select(g => new Assembly(
                g,
                Math.Min(g.Sum(i => parts[i].ReferenceMatches), framePixels * 0.15f),
                g.Any(i => parts[i].Truncated)
            ))
            .Where(g => g.Value > 0)
            .ToArray();
        if (assemblies.Length == 0)
            return PortraitBreakoutPlan.Empty(grid.Frame);
        var membership = Enumerable.Repeat(-1, parts.Length).ToArray();
        for (var i = 0; i < assemblies.Length; i++)
            foreach (var part in assemblies[i].Parts)
                membership[part] = i;
        var totalValue = assemblies.Sum(g => g.Value);
        var best = PortraitBreakoutPlan.Empty(grid.Frame);
        var bestScore = float.NegativeInfinity;
        var centerX = (grid.Frame.Left + grid.Frame.Right) * 0.5f;
        var centerY = (grid.Frame.Bottom + grid.Frame.Top) * 0.5f;
        foreach (var scale in new[] { 1f, 0.97f, 0.94f, 0.91f })
        foreach (var shift in scale == 1f ? new[] { 0f } : new[] { 0f, -0.015f })
        {
            cancellation.ThrowIfCancellationRequested();
            var x = centerX * (1 - scale);
            var y = centerY * (1 - scale) + shift * grid.Frame.Height;
            var projections = Project(
                grid,
                coverage,
                membership,
                assemblies.Length,
                interior,
                scale,
                x,
                y,
                cancellation
            );
            var selected = new bool[parts.Length];
            var admitted = new bool[assemblies.Length];
            var union = new bool[interior.Length];
            var poseAreas = new int[coverage.Length];
            var poseUnion = Enumerable
                .Range(0, coverage.Length)
                .Select(_ => new uint[(interior.Length + 31) / 32])
                .ToArray();
            var value = 0f;
            foreach (
                var i in Enumerable
                    .Range(0, assemblies.Length)
                    .OrderByDescending(i =>
                        assemblies[i].Value
                        / Math.Sqrt(Math.Max(1, projections[i].Mask.Count(p => p)))
                    )
            )
            {
                var assembly = assemblies[i];
                var projection = projections[i];
                if (assembly.Truncated || !projection.Complete)
                    continue;
                if (projection.Areas.All(a => a <= 2))
                {
                    value += assembly.Value;
                    continue;
                }
                var count = 0;
                for (var p = 0; p < union.Length; p++)
                    if (union[p] || projection.Mask[p])
                        count++;
                if (count > framePixels * PortraitBreakoutPlan.AreaBudget)
                    continue;
                var additions = new int[coverage.Length];
                for (var pose = 0; pose < coverage.Length; pose++)
                    foreach (var pixel in projection.PosePixels[pose])
                        if ((poseUnion[pose][pixel >> 5] & (1u << (pixel & 31))) == 0)
                            additions[pose]++;
                if (
                    additions
                        .Where(
                            (area, pose) =>
                                area + poseAreas[pose]
                                > framePixels * PortraitBreakoutPlan.PoseAreaBudget
                        )
                        .Any()
                )
                    continue;
                foreach (var part in assembly.Parts)
                    selected[part] = true;
                admitted[i] = true;
                value += assembly.Value;
                for (var p = 0; p < union.Length; p++)
                    union[p] |= projection.Mask[p];
                for (var p = 0; p < poseAreas.Length; p++)
                {
                    poseAreas[p] += additions[p];
                    foreach (var pixel in projection.PosePixels[p])
                        poseUnion[p][pixel >> 5] |= 1u << (pixel & 31);
                }
            }
            value -= TrimSplits(assemblies, admitted, selected, drawOrders);
            Array.Clear(union, 0, union.Length);
            for (var i = 0; i < assemblies.Length; i++)
                if (admitted[i])
                    for (var p = 0; p < union.Length; p++)
                        union[p] |= projections[i].Mask[p];
            // Prefer the native framing unless a small change preserves substantially more
            // of its supported objects. All attempts reuse owner maps; no new rasterization.
            var score = value - totalValue * ((1 - scale) * 2f + Math.Abs(shift) * 2f);
            if (score <= bestScore)
                continue;
            bestScore = score;
            var allowed = parts
                .Where((p, i) => selected[i])
                .Select(p => p.Name)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            if (allowed.Length == 0)
                best = PortraitBreakoutPlan.Empty(grid.Frame) with
                {
                    ContentScale = scale,
                    ContentX = x,
                    ContentY = y,
                };
            else
                best = grid with
                {
                    Parts = allowed,
                    Runs = Runs(union),
                    ContentScale = scale,
                    ContentX = x,
                    ContentY = y,
                };
            if (scale == 1 && value >= totalValue)
                return best;
        }
        return best;
    }

    private static Projection[] Project(
        PortraitBreakoutPlan grid,
        PortraitBreakoutCoverage[][] poses,
        int[] membership,
        int count,
        bool[] interior,
        float scale,
        float offsetX,
        float offsetY,
        CancellationToken cancellation
    )
    {
        var mapping = new int[interior.Length];
        var complete = new bool[mapping.Length];
        for (var p = 0; p < mapping.Length; p++)
        {
            var x =
                (grid.Bounds.Left + (p % grid.Width + 0.5f) * grid.Bounds.Width / grid.Width)
                    * scale
                + offsetX;
            var y =
                (grid.Bounds.Bottom + (p / grid.Width + 0.5f) * grid.Bounds.Height / grid.Height)
                    * scale
                + offsetY;
            var nx = (int)MathF.Floor((x - grid.Bounds.Left) * grid.Width / grid.Bounds.Width);
            var ny = (int)MathF.Floor((y - grid.Bounds.Bottom) * grid.Height / grid.Bounds.Height);
            complete[p] =
                PortraitBreakoutPlan.WithinEnvelope(grid.Frame, x, y)
                && PortraitBreakoutPlan.WithinEnvelope(
                    grid.Frame,
                    grid.Bounds.Left + (nx + 0.5f) * grid.Bounds.Width / grid.Width,
                    grid.Bounds.Bottom + (ny + 0.5f) * grid.Bounds.Height / grid.Height
                );
            mapping[p] =
                nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height
                    ? -1
                    : ny * grid.Width + nx;
        }
        var result = Enumerable
            .Range(0, count)
            .Select(_ => new Projection(
                new bool[interior.Length],
                new int[poses.Length],
                new uint[(interior.Length + 31) / 32],
                new int[poses.Length][]
            ))
            .ToArray();
        for (var pose = 0; pose < poses.Length; pose++)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var projection in result)
                Array.Clear(projection.Stamp, 0, projection.Stamp.Length);
            var pixels = new List<int>[count];
            foreach (var surface in poses[pose])
            {
                var assembly = membership[surface.Part];
                if (assembly < 0)
                    continue;
                var projection = result[assembly];
                foreach (var p in surface.Pixels)
                {
                    if (!complete[p])
                        projection.Complete = false;
                    var mapped = mapping[p];
                    if (mapped < 0 || interior[mapped])
                        continue;
                    var bit = 1u << (mapped & 31);
                    var word = mapped >> 5;
                    // Preserve overlapping and translucent surfaces independently of
                    // who happened to be the visible opaque owner in the sampled pose.
                    if ((projection.Stamp[word] & bit) == 0)
                    {
                        projection.Stamp[word] |= bit;
                        projection.Areas[pose]++;
                        projection.Mask[mapped] = true;
                        (pixels[assembly] ??= new List<int>()).Add(mapped);
                    }
                }
            }
            for (var assembly = 0; assembly < count; assembly++)
                result[assembly].PosePixels[pose] = pixels[assembly]?.ToArray() ?? [];
        }
        foreach (var projection in result)
            Dilate(projection.Mask, interior, grid);
        return result;
    }

    private static void Dilate(bool[] pixels, bool[] interior, PortraitBreakoutPlan grid)
    {
        var width = grid.Width;
        var height = grid.Height;
        var source = (bool[])pixels.Clone();
        for (var p = 0; p < source.Length; p++)
        {
            if (!source[p])
                continue;
            var x = p % width;
            var y = p / width;
            for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
            {
                if (dx * dx + dy * dy > 9)
                    continue;
                var nx = x + dx;
                var ny = y + dy;
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    var index = ny * width + nx;
                    if (
                        !interior[index]
                        && PortraitBreakoutPlan.WithinEnvelope(
                            grid.Frame,
                            grid.Bounds.Left + (nx + 0.5f) * grid.Bounds.Width / width,
                            grid.Bounds.Bottom + (ny + 0.5f) * grid.Bounds.Height / height
                        )
                    )
                        pixels[index] = true;
                }
            }
        }
    }

    private static int[] Runs(bool[] pixels)
    {
        var runs = new List<int>();
        for (var i = 0; i < pixels.Length; i++)
        {
            if (!pixels[i])
                continue;
            var start = i;
            while (i + 1 < pixels.Length && pixels[i + 1])
                i++;
            runs.Add(start);
            runs.Add(i - start + 1);
        }
        return runs.ToArray();
    }

    private static float TrimSplits(
        Assembly[] assemblies,
        bool[] admitted,
        bool[] selected,
        int[][] orders
    )
    {
        var removedValue = 0f;
        int Overflow() =>
            orders.Sum(order =>
                Math.Max(0, PartCount(order, selected) - PortraitBreakoutPlan.MaxParts)
            );
        for (var overflow = Overflow(); overflow > 0; overflow = Overflow())
        {
            var best = -1;
            var bestCost = float.PositiveInfinity;
            for (var i = 0; i < assemblies.Length; i++)
            {
                if (!admitted[i])
                    continue;
                foreach (var part in assemblies[i].Parts)
                    selected[part] = false;
                var reduction = overflow - Overflow();
                foreach (var part in assemblies[i].Parts)
                    selected[part] = true;
                var cost =
                    assemblies[i].Value / Math.Max(1, reduction) * (reduction > 0 ? 1 : 1000);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            if (best < 0)
                break;
            admitted[best] = false;
            foreach (var part in assemblies[best].Parts)
                selected[part] = false;
            removedValue += assemblies[best].Value;
        }
        return removedValue;
    }

    private static int PartCount(int[] order, bool[] selected)
    {
        bool? previous = null;
        var count = 0;
        foreach (var id in order)
        {
            var allowed = id >= 0 && selected[id];
            if (previous != allowed)
            {
                count++;
                previous = allowed;
            }
        }
        return count;
    }
}
