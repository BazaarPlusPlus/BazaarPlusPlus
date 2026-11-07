#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal static class PortraitBreakoutAnalyzer
{
    internal static PortraitBreakoutPlan Analyze(
        PortraitCompositionInput input,
        PortraitComposition fit,
        PortraitFrameGeometry frame,
        CancellationToken cancellation
    )
    {
        if (
            !frame.IsValid
            || input.Poses.Length is 0 or > 64
            || input.Poses.Any(p => !p.BreakoutSupported || p.Triangles.Length > 10_000)
        )
            return PortraitBreakoutPlan.Empty(frame);
        var first = input
            .Poses.SelectMany(p => p.Triangles)
            .Where(t => t.Part != null)
            .GroupBy(t => t.Part!, StringComparer.Ordinal)
            .Take(513)
            .Select(g => g.First())
            .ToArray();
        if (first.Length is 0 or > 512)
            return PortraitBreakoutPlan.Empty(frame);
        var eligible = input
            .Poses.SelectMany(p => p.Triangles)
            .Where(t => t.BreakoutEligible)
            .Select(t => t.Part)
            .ToHashSet(StringComparer.Ordinal);
        var parts = first
            .Select(t => new PortraitBreakoutPart(t.Part!, t.Rig, input.Poses.Length)
            {
                Rejected = !eligible.Contains(t.Part),
            })
            .ToArray();
        var ids = parts.Select((p, id) => (p.Name, id)).ToDictionary(p => p.Name, p => p.id);
        // Discovery must include whole tips and the farthest animation poses. Only the
        // selected, area-bounded exterior runs become visible at runtime.
        var bounds = new PortraitFrameGeometry(
            frame.Left - frame.Width * (PortraitBreakoutPlan.SideLimit + 0.05f),
            frame.Bottom,
            frame.Right + frame.Width * (PortraitBreakoutPlan.SideLimit + 0.05f),
            frame.Top + frame.Height * (PortraitBreakoutPlan.TopLimit + 0.05f)
        );
        var scale = PortraitBreakoutPlan.Resolution / Math.Max(bounds.Width, bounds.Height);
        var width = Math.Min(
            PortraitBreakoutPlan.Resolution,
            Math.Max(8, (int)MathF.Ceiling(bounds.Width * scale))
        );
        var height = Math.Min(
            PortraitBreakoutPlan.Resolution,
            Math.Max(8, (int)MathF.Ceiling(bounds.Height * scale))
        );
        var plan = new PortraitBreakoutPlan(frame, bounds, width, height, [], []);
        var interior = plan.Interior();
        var framePixels = interior.Count(p => p);
        var referencePose = input.Poses.OrderBy(p => Math.Abs(p.Time - fit.PoseTime)).First();
        var reference = PortraitBreakoutRasterizer.Render(
            input.Pages,
            referencePose,
            fit,
            plan,
            ids,
            cancellation,
            captureCoverage: true
        );
        if (reference == null)
            return PortraitBreakoutPlan.Empty(frame);
        var contacts = new HashSet<int>();
        var exteriorContacts = new HashSet<int>();
        PortraitBreakoutGroups.AddContacts(
            reference.Owners,
            width,
            height,
            contacts,
            reference: true,
            interior,
            exteriorContacts
        );
        for (var i = 0; i < interior.Length; i++)
        {
            var id = reference.Owners[i];
            if (id < 0)
                continue;
            var part = parts[id];
            part.ReferencePixels++;
            if (!interior[i])
                part.ReferenceOutside++;
            var x = bounds.Left + (i % width + 0.5f) * bounds.Width / width;
            var y = bounds.Bottom + (i / width + 0.5f) * bounds.Height / height;
            var comparison = Compare(input.Reference, reference.Pixels, i, x, y);
            if (comparison != 0)
            {
                part.ReferenceComparable++;
                if (!interior[i])
                    part.OutsideComparable++;
            }
            if (comparison > 0)
            {
                part.ReferenceMatches++;
                if (!interior[i])
                    part.OutsideMatches++;
            }
        }
        var drawOrders = new int[input.Poses.Length][];
        var coverage = new PortraitBreakoutCoverage[input.Poses.Length][];
        var totalCoverage = 0;
        for (var poseIndex = 0; poseIndex < input.Poses.Length; poseIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var pose = input.Poses[poseIndex];
            var rendered = ReferenceEquals(pose, referencePose)
                ? reference
                : PortraitBreakoutRasterizer.Render(
                    input.Pages,
                    pose,
                    fit,
                    plan,
                    ids,
                    cancellation,
                    captureCoverage: true
                );
            if (rendered == null)
                return PortraitBreakoutPlan.Empty(frame);
            if (!ReferenceEquals(pose, referencePose))
                PortraitBreakoutGroups.AddContacts(
                    rendered.Owners,
                    width,
                    height,
                    contacts,
                    reference: false,
                    interior,
                    exteriorContacts
                );
            drawOrders[poseIndex] = DrawOrder(pose, ids);
            coverage[poseIndex] = rendered.Coverage;
            totalCoverage += rendered.Coverage.Sum(p => p.Pixels.Length);
            if (totalCoverage > 4_000_000)
                return PortraitBreakoutPlan.Empty(frame);
            foreach (var surface in rendered.Coverage)
            {
                var part = parts[surface.Part];
                part.Truncated |= surface.Truncated;
                foreach (var i in surface.Pixels)
                {
                    if (interior[i])
                        continue;
                    part.Exterior ??= new bool[interior.Length];
                    part.Exterior[i] = true;
                    part.Areas[poseIndex]++;
                }
            }
            if (!RejectBottomCuts(input.Pages, pose, fit, plan, ids, parts, cancellation))
                return PortraitBreakoutPlan.Empty(frame);
        }
        foreach (var part in parts)
        {
            part.Contradicted =
                (
                    part.ReferenceComparable >= 12
                    && part.ReferenceMatches < part.ReferenceComparable * 0.65f
                )
                || (
                    part.OutsideComparable > 5
                    && part.OutsideMatches < part.OutsideComparable * 0.5f
                );
            part.Rejected |=
                part.ReferencePixels - part.ReferenceOutside > framePixels * 0.45f
                || (
                    part.Rig is not { Bone: > 0 }
                    && part.Exterior != null
                    && Broad(part.Exterior, width, height, frame, bounds)
                );
        }
        RejectCanvasCuts(input.Reference, reference, plan, parts);
        var groups = PortraitBreakoutGroups.Build(parts, contacts, exteriorContacts, framePixels);
        return PortraitBreakoutSelection.Select(
            plan,
            parts,
            groups,
            coverage,
            drawOrders,
            cancellation
        );
    }

    private static bool RejectBottomCuts(
        PortraitImage[] pages,
        PortraitPose pose,
        PortraitComposition fit,
        PortraitBreakoutPlan plan,
        Dictionary<string, int> ids,
        PortraitBreakoutPart[] parts,
        CancellationToken cancellation
    )
    {
        var frame = plan.Frame;
        var reach = frame.Height * 0.08f;
        // Look below the native crop without adding those pixels to display coverage,
        // its exterior budget, or the renderer's mask.
        var grid = plan with
        {
            Bounds = plan.Bounds with { Bottom = frame.Bottom - reach, Top = frame.Bottom + reach },
            Height = Math.Max(8, (int)MathF.Ceiling(2 * reach * plan.Height / plan.Bounds.Height)),
        };
        var rendered = PortraitBreakoutRasterizer.Render(
            pages,
            pose,
            fit,
            grid,
            ids,
            cancellation,
            captureCoverage: true,
            coverageAlphaCutoff: 220
        );
        if (rendered == null)
            return false;
        var pixelWidth = grid.Bounds.Width / grid.Width;
        var pixelHeight = grid.Bounds.Height / grid.Height;
        var depth = Math.Max(frame.Height * 0.02f, 2 * pixelHeight);
        var minimumWidth = Math.Max(3, (int)MathF.Ceiling(frame.Width * 0.04f / pixelWidth));
        var cutRow = (int)MathF.Ceiling((frame.Bottom - grid.Bounds.Bottom) / pixelHeight - 0.5f);
        foreach (var surface in rendered.Coverage)
        {
            cancellation.ThrowIfCancellationRequested();
            if (parts[surface.Part].Rejected || surface.Pixels.Length < minimumWidth)
                continue;
            var pixels = new bool[grid.Width * grid.Height];
            foreach (var pixel in surface.Pixels)
                pixels[pixel] = true;
            var crossing = new bool[grid.Width];
            for (var x = 0; x < grid.Width; x++)
                crossing[x] =
                    pixels[cutRow * grid.Width + x] && pixels[(cutRow - 1) * grid.Width + x];
            var queue = new int[surface.Pixels.Length];
            foreach (var start in surface.Pixels)
            {
                if (!pixels[start])
                    continue;
                var count = 1;
                queue[0] = start;
                pixels[start] = false;
                var below = false;
                var above = false;
                var side = false;
                var columns = new List<int>();
                for (var cursor = 0; cursor < count; cursor++)
                {
                    var pixel = queue[cursor];
                    var column = pixel % grid.Width;
                    var row = pixel / grid.Width;
                    var x = grid.Bounds.Left + (column + 0.5f) * pixelWidth;
                    var y = grid.Bounds.Bottom + (row + 0.5f) * pixelHeight;
                    below |= y <= frame.Bottom - depth;
                    above |= y >= frame.Bottom + depth;
                    side |=
                        y > frame.Bottom + pixelHeight
                        && (x < frame.Left - pixelWidth || x > frame.Right + pixelWidth);
                    if (row == cutRow && crossing[column])
                        columns.Add(column);
                    if (column > 0)
                        Visit(pixel - 1);
                    if (column + 1 < grid.Width)
                        Visit(pixel + 1);
                    if (row > 0)
                        Visit(pixel - grid.Width);
                    if (row + 1 < grid.Height)
                        Visit(pixel + grid.Width);
                }
                if (!below || !above || !side)
                    continue;
                columns.Sort();
                var run = 0;
                var previous = -2;
                foreach (var column in columns)
                {
                    run = column == previous + 1 ? run + 1 : 1;
                    previous = column;
                    if (run >= minimumWidth)
                    {
                        // Preserve the native bottom crop even when shrinking could lift
                        // it. This part loses breakout; unrelated parts remain eligible.
                        parts[surface.Part].Rejected = true;
                        break;
                    }
                }
                if (parts[surface.Part].Rejected)
                    break;

                void Visit(int pixel)
                {
                    if (!pixels[pixel])
                        return;
                    pixels[pixel] = false;
                    queue[count++] = pixel;
                }
            }
        }
        return true;
    }

    private static void RejectCanvasCuts(
        PortraitImage image,
        PortraitBreakoutRaster raster,
        PortraitBreakoutPlan grid,
        PortraitBreakoutPart[] parts
    )
    {
        var left = image.Width;
        var right = -1;
        var top = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            if (Opaque(x, y))
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Max(top, y);
            }
        if (right < left)
            return;
        // Exported artwork can have transparent padding after its authored cut.
        // Alpha bounds nominate planes only; keep the original canvas coordinates.
        Scan(true, left, -1);
        Scan(true, right, 1);
        Scan(false, top, 1);

        bool Opaque(int x, int y) =>
            x >= 0
            && x < image.Width
            && y >= 0
            && y < image.Height
            && image.Rgba[(y * image.Width + x) * 4 + 3] >= 220;

        void Scan(bool vertical, int edge, int outward)
        {
            var extent = (vertical ? grid.Frame.Height : grid.Frame.Width) * image.Height;
            var minimumBand = Math.Max(6, (int)MathF.Ceiling(extent * 0.1f));
            var minimumPart = Math.Max(3, (int)MathF.Ceiling(extent * 0.03f));
            var depth = Math.Max(
                3f / image.Height,
                2 * (vertical ? grid.Bounds.Width / grid.Width : grid.Bounds.Height / grid.Height)
            );
            var inward = (int)MathF.Ceiling(depth * image.Height);
            var candidates = new HashSet<int>();
            var band = 0;
            var previous = -1;
            var run = 0;
            var length = vertical ? image.Height : image.Width;
            for (var along = 0; along <= length; along++)
            {
                var opaque = along < length;
                for (var d = 0; opaque && d <= inward; d++)
                    opaque = vertical
                        ? Opaque(edge - outward * d, along)
                        : Opaque(along, edge - outward * d);
                if (!opaque)
                {
                    if (band >= minimumBand)
                        foreach (var candidate in candidates)
                            parts[candidate].Rejected = true;
                    candidates.Clear();
                    band = run = 0;
                    previous = -1;
                    continue;
                }
                band++;
                var id = ContinuingPart(vertical, edge, along, outward, depth);
                run = id >= 0 ? (id == previous ? run + 1 : 1) : 0;
                previous = id;
                if (run >= minimumPart)
                    candidates.Add(id);
            }
        }

        int ContinuingPart(bool vertical, int edge, int along, int outward, float depth)
        {
            var id = -1;
            for (var step = -2; step <= 2; step++)
            {
                var across = (edge + 0.5f) / image.Height + outward * depth * step / 2;
                var parallel = (along + 0.5f) / image.Height;
                var x = vertical ? across : parallel;
                var y = vertical ? parallel : across;
                var column = (int)
                    MathF.Floor((x - grid.Bounds.Left) * grid.Width / grid.Bounds.Width);
                var row = (int)
                    MathF.Floor((y - grid.Bounds.Bottom) * grid.Height / grid.Bounds.Height);
                if (column < 0 || column >= grid.Width || row < 0 || row >= grid.Height)
                    return -1;
                var pixel = row * grid.Width + column;
                var owner = raster.Owners[pixel];
                // A complete silhouette can touch the same plane. Require opaque
                // continuation by the same part well beyond raster/fit tolerance,
                // and matching static artwork on the inward side of the cut.
                if (owner < 0 || (step > -2 && owner != id))
                    return -1;
                if (step == -2 && Compare(image, raster.Pixels, pixel, x, y) <= 0)
                    return -1;
                id = owner;
            }
            return id;
        }
    }

    private static int[] DrawOrder(PortraitPose pose, Dictionary<string, int> ids)
    {
        var result = new List<int>();
        foreach (var name in pose.DrawOrder ?? pose.Triangles.Select(t => t.Part ?? "").ToArray())
        {
            var id = ids.TryGetValue(name, out var found) ? found : -1;
            if (result.Count == 0 || result[result.Count - 1] != id)
                result.Add(id);
        }
        return result.ToArray();
    }

    // Zero means the static canvas supplies no comparable artwork at this location.
    // Unknown regions can inherit a local rig anchor; color contradictions cannot.
    private static int Compare(
        PortraitImage reference,
        byte[] rendered,
        int pixel,
        float x,
        float y
    )
    {
        var cx = (int)MathF.Floor(x * reference.Height);
        var cy = (int)MathF.Floor(y * reference.Height);
        var observed = false;
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            var nx = cx + dx;
            var ny = cy + dy;
            if (nx < 0 || nx >= reference.Width || ny < 0 || ny >= reference.Height)
                continue;
            var index = (ny * reference.Width + nx) * 4;
            if (reference.Rgba[index + 3] < 220)
                continue;
            observed = true;
            var error = 0;
            for (var c = 0; c < 3; c++)
                error += Math.Abs(reference.Rgba[index + c] - rendered[pixel * 4 + c]);
            if (error <= 135)
                return 1;
        }
        return observed ? -1 : 0;
    }

    private static bool Broad(
        bool[] pixels,
        int width,
        int height,
        PortraitFrameGeometry frame,
        PortraitFrameGeometry bounds
    )
    {
        for (var y = 0; y < height; y++)
        {
            var count = 0;
            for (var x = 0; x < width; x++)
                if (pixels[y * width + x])
                    count++;
            if (count * bounds.Width / width > frame.Width * 0.55f)
                return true;
        }
        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var y = 0; y < height; y++)
                if (pixels[y * width + x])
                    count++;
            if (count * bounds.Height / height > frame.Height * 0.5f)
                return true;
        }
        return false;
    }
}
