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
        PortraitBreakoutGroups.AddContacts(
            reference.Owners,
            width,
            height,
            contacts,
            reference: true
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
                    reference: false
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
        var groups = PortraitBreakoutGroups.Build(parts, contacts, framePixels);
        return PortraitBreakoutSelection.Select(
            plan,
            parts,
            groups,
            coverage,
            drawOrders,
            cancellation
        );
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
