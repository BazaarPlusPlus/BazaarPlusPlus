#nullable enable
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal static class PortraitBreakoutAnalyzer
{
    private sealed class Evidence
    {
        internal int Pixels,
            Matches,
            Comparable,
            Outside,
            OutsideMatches,
            OutsideComparable;
        internal bool Exterior => OutsideMatches >= 2 && OutsideMatches >= OutsideComparable * 0.6f;
        internal bool Interior => Matches >= 6 && Matches >= Comparable * 0.7f;
        internal float Support => Matches / (float)Math.Max(1, Pixels);
        internal bool ReliableView =>
            Pixels >= 12 && Comparable >= Pixels * 0.9f && Matches >= Comparable * 0.8f;
    }

    private sealed class Surface
    {
        internal readonly string Name;
        internal readonly int Id,
            Page,
            Width,
            Height;
        internal readonly PortraitFrameGeometry Uv;
        internal readonly PortraitRigFamily? Rig;
        internal readonly Evidence[] Evidence;
        internal readonly bool Eligible;
        internal readonly PortraitTriangle[] Geometry;

        internal Surface(
            string name,
            int id,
            PortraitTriangle[] triangles,
            int poses,
            PortraitImage image
        )
        {
            Name = name;
            Id = id;
            Page = triangles[0].Page;
            Eligible = triangles.All(t => t.Page == Page) && triangles.Any(t => t.BreakoutEligible);
            Rig = triangles[0].Rig == null ? null : PortraitRigFamily.From(triangles[0].Rig!);
            var uvs = triangles.SelectMany(t => new[] { t.UvA, t.UvB, t.UvC }).ToArray();
            Uv = new(uvs.Min(p => p.X), uvs.Min(p => p.Y), uvs.Max(p => p.X), uvs.Max(p => p.Y));
            Width = Math.Max(8, Math.Min(96, (int)MathF.Ceiling(Uv.Width * image.Width)));
            Height = Math.Max(8, Math.Min(96, (int)MathF.Ceiling(Uv.Height * image.Height)));
            Geometry = triangles
                .GroupBy(t => (t.UvA, t.UvB, t.UvC))
                .Select(g => g.First())
                .ToArray();
            Evidence = Enumerable.Range(0, poses).Select(_ => new Evidence()).ToArray();
        }
    }

    private sealed record Cut(bool Vertical, float Edge, int Outward, float Low, float High);

    internal static PortraitBreakoutPlan Analyze(
        PortraitCompositionInput input,
        PortraitComposition fit,
        PortraitFrameGeometry frame,
        CancellationToken cancellation
    )
    {
        cancellation.ThrowIfCancellationRequested();
        if (
            !frame.IsValid
            || input.Poses.Length is 0 or > 64
            || input.Pages.Length is 0 or > 4
            || input.Poses.Any(p => !p.BreakoutSupported || p.Triangles.Length > 10_000)
        )
            return PortraitBreakoutPlan.Empty(frame);
        var groups = input
            .Poses.SelectMany(p => p.Triangles)
            .Where(t => t.Part != null)
            .GroupBy(t => t.Part!, StringComparer.Ordinal)
            .Take(513)
            .ToArray();
        if (
            groups.Length is 0 or > 512
            || groups.Any(g => g.Any(t => t.Page < 0 || t.Page >= input.Pages.Length))
        )
            return PortraitBreakoutPlan.Empty(frame);
        var surfaces = groups
            .Select(
                (g, id) =>
                    new Surface(
                        g.Key,
                        id,
                        g.ToArray(),
                        input.Poses.Length,
                        input.Pages[g.First().Page]
                    )
            )
            .ToArray();
        if (surfaces.Sum(s => s.Width * s.Height) > 2_000_000)
            return PortraitBreakoutPlan.Empty(frame);
        var ids = surfaces.ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);
        var bounds = new PortraitFrameGeometry(
            frame.Left - frame.Width * 0.45f,
            frame.Bottom,
            frame.Right + frame.Width * 0.45f,
            frame.Top + frame.Height * 0.70f
        );
        var density = PortraitBreakoutPlan.Resolution / Math.Max(bounds.Width, bounds.Height);
        var grid = new PortraitBreakoutPlan(
            frame,
            bounds,
            Math.Min(192, (int)MathF.Ceiling(bounds.Width * density)),
            Math.Min(192, (int)MathF.Ceiling(bounds.Height * density)),
            [],
            []
        );
        var interior = grid.Interior();
        var framePixels = interior.Count(p => p);
        var referenceIndex = Enumerable
            .Range(0, input.Poses.Length)
            .OrderBy(i => Math.Abs(input.Poses[i].Time - fit.PoseTime))
            .First();
        PortraitBreakoutRaster? reference = null;
        var coverage = new int[input.Poses.Length][];
        for (var pose = 0; pose < input.Poses.Length; pose++)
        {
            var raster = PortraitBreakoutRasterizer.Render(
                input.Pages,
                input.Poses[pose],
                fit,
                grid,
                ids,
                cancellation
            );
            if (raster == null)
                return PortraitBreakoutPlan.Empty(frame);
            if (pose == referenceIndex)
                reference = raster;
            var cells = coverage[pose] = Enumerable.Repeat(-1, interior.Length).ToArray();
            for (var pixel = 0; pixel < raster.Owners.Length; pixel++)
            {
                var id = raster.Owners[pixel];
                if (id < 0)
                    continue;
                var evidence = surfaces[id].Evidence[pose];
                evidence.Pixels++;
                var point = Point(grid, pixel);
                var comparison = Compare(input.Reference, raster.Pixels, pixel, point);
                if (comparison != 0)
                    evidence.Comparable++;
                if (comparison > 0)
                    evidence.Matches++;
                if (interior[pixel])
                    continue;
                var surface = surfaces[id];
                var uv = raster.Uvs[pixel];
                var column = (int)
                    MathF.Floor((uv.X - surface.Uv.Left) * surface.Width / surface.Uv.Width);
                var row = (int)
                    MathF.Floor((uv.Y - surface.Uv.Bottom) * surface.Height / surface.Uv.Height);
                if (column >= 0 && column < surface.Width && row >= 0 && row < surface.Height)
                    cells[pixel] = id * 16384 + row * surface.Width + column;
                evidence.Outside++;
                if (comparison != 0)
                    evidence.OutsideComparable++;
                if (comparison > 0)
                    evidence.OutsideMatches++;
            }
        }
        var anchors = surfaces
            .Where(s => s.Eligible && s.Evidence[referenceIndex].Exterior && s.Rig is { Bone: > 0 })
            .Select(s => s.Rig!.Bone)
            .ToHashSet();
        var rasters = new Dictionary<int, PortraitBreakoutRaster> { [referenceIndex] = reference! };
        var cuts = CanvasCuts(input.Reference, frame);
        var regions = new List<PortraitBreakoutRegion>();
        foreach (var surface in surfaces)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!surface.Eligible || surface.Uv.Width <= 0 || surface.Uv.Height <= 0)
                continue;
            var evidence = surface.Evidence[referenceIndex];
            var localIndex = referenceIndex;
            if (!evidence.Exterior)
            {
                // Small moving details can disagree with the whole-character pose. Require
                // several visible exterior matches before borrowing a different local pose.
                if (
                    evidence.Pixels <= Math.Max(12, framePixels * 0.01f)
                    && evidence.Comparable >= 12
                    && evidence.Matches >= evidence.Comparable * 0.5f
                )
                {
                    var best = Enumerable
                        .Range(0, input.Poses.Length)
                        .Where(i =>
                            surface.Evidence[i].OutsideMatches >= 3
                            && surface.Evidence[i].OutsideMatches
                                >= surface.Evidence[i].OutsideComparable * 0.7f
                        )
                        .OrderByDescending(i => surface.Evidence[i].OutsideMatches)
                        .ThenBy(i => Math.Abs(input.Poses[i].Time - fit.PoseTime))
                        .DefaultIfEmpty(-1)
                        .First();
                    if (best >= 0)
                        localIndex = best;
                }
                if (
                    localIndex == referenceIndex
                    && !(
                        evidence.Interior
                        && evidence.OutsideComparable == 0
                        && surface.Rig is { Bone: > 0 } rig
                        && anchors.Contains(rig.Bone)
                    )
                )
                    continue;
            }
            // Admission and registration are separate decisions. An independently
            // moving prop can match a different pose without gaining new UV permission.
            var preserveUnobserved = localIndex != referenceIndex;
            var observation = Observe(
                surface,
                localIndex,
                input,
                fit,
                grid,
                ids,
                rasters,
                cancellation
            );
            if (observation == null)
                return PortraitBreakoutPlan.Empty(frame);
            var candidates = Enumerable
                .Range(0, input.Poses.Length)
                .Where(i =>
                    surface.Evidence[i].ReliableView
                    && surface.Evidence[i].Support > surface.Evidence[localIndex].Support + 0.1f
                    && surface.Evidence[i].Pixels >= surface.Evidence.Max(e => e.Pixels) * 0.6f
                )
                .OrderByDescending(i => surface.Evidence[i].Support)
                .Take(3)
                .ToArray();
            if (candidates.Length != 0)
            {
                var core = CoreFootprint(surface, input.Pages[surface.Page], cancellation);
                foreach (var candidate in candidates)
                {
                    var proposed = Observe(
                        surface,
                        candidate,
                        input,
                        fit,
                        grid,
                        ids,
                        rasters,
                        cancellation
                    );
                    if (
                        proposed != null
                        && FullyObserved(surface, proposed, core, input.Reference, grid)
                    )
                    {
                        observation = proposed;
                        break;
                    }
                }
            }
            var local = observation.Raster;
            var points = observation.Points;
            var mapped = observation.Mapped;
            var opaque = new bool[points.Length];
            var allowed = new bool[points.Length];
            var forbidden = new bool[points.Length];
            var below = 0;
            var above = 0;
            for (var i = 0; i < points.Length; i++)
            {
                if (mapped[i] != 1)
                    continue;
                var uv = Uv(surface, i);
                var page = input.Pages[surface.Page];
                var tx = Math.Max(0, Math.Min(page.Width - 1, (int)(uv.X * page.Width)));
                var ty = Math.Max(0, Math.Min(page.Height - 1, (int)(uv.Y * page.Height)));
                var alpha = page.Rgba[(ty * page.Width + tx) * 4 + 3];
                opaque[i] = alpha >= 8;
                if (alpha >= 220)
                {
                    if (points[i].Y < frame.Bottom - frame.Height * 0.02f)
                        below++;
                    if (points[i].Y > frame.Bottom + frame.Height * 0.02f)
                        above++;
                }
                forbidden[i] = cuts.Any(c => Beyond(c, points[i]));
                if (!opaque[i] || forbidden[i])
                    continue;
                if (frame.Contains(points[i].X, points[i].Y))
                {
                    allowed[i] = true;
                    continue;
                }
                var pixel = Pixel(grid, points[i]);
                allowed[i] =
                    pixel >= 0
                    && local.Owners[pixel] == surface.Id
                    && Compare(input.Reference, local.Pixels, pixel, points[i]) > 0;
            }
            // Only the local material around the authored bottom is constrained. A head
            // sharing this attachment never inherits a lower skirt's all-or-nothing veto.
            var bottomCrop = below >= 3 && above >= 3;
            if (bottomCrop)
                for (var i = 0; i < points.Length; i++)
                    if (mapped[i] == 1 && points[i].Y < frame.Bottom + frame.Height * 0.08f)
                        forbidden[i] = true;
            var material = opaque.Select((p, i) => p && !forbidden[i] && mapped[i] == 1).ToArray();
            var components = Components(material, surface.Width, surface.Height);
            var expanded = new bool[allowed.Length];
            for (var i = 0; i < expanded.Length; i++)
                if (!forbidden[i] && mapped[i] == 1)
                    expanded[i] = allowed[i] || preserveUnobserved;
            // Preserve local fringes without treating downsampled transparent atlas
            // pixels as proof that the full-resolution material is empty.
            for (var y = 0; y < surface.Height; y++)
            for (var x = 0; x < surface.Width; x++)
            {
                var i = y * surface.Width + x;
                if (expanded[i] || forbidden[i] || mapped[i] != 1)
                    continue;
                for (var dy = -1; dy <= 1 && !expanded[i]; dy++)
                for (var dx = -1; dx <= 1 && !expanded[i]; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx >= 0 && nx < surface.Width && ny >= 0 && ny < surface.Height)
                    {
                        var neighbor = ny * surface.Width + nx;
                        expanded[i] |=
                            allowed[neighbor]
                            && (!opaque[i] || components[i] == components[neighbor]);
                    }
                }
            }
            if (!expanded.Any(p => p))
                continue;
            regions.Add(
                new PortraitBreakoutRegion(
                    surface.Name,
                    surface.Page,
                    surface.Uv,
                    surface.Width,
                    surface.Height,
                    Runs(expanded)
                )
                {
                    BottomCrop = bottomCrop,
                }
            );
        }
        if (regions.Count == 0)
            return PortraitBreakoutPlan.Empty(frame);
        // This swept silhouette is diagnostic/cache metadata only. Playback clips each
        // live native triangle by its own UV region, never by this shared union.
        var union = new bool[interior.Length];
        var byName = regions.ToDictionary(r => r.Part, StringComparer.Ordinal);
        foreach (var cells in coverage)
            for (var i = 0; i < union.Length; i++)
                if (cells[i] >= 0)
                {
                    var surface = surfaces[cells[i] / 16384];
                    if (
                        !byName.TryGetValue(surface.Name, out var region)
                        || !region.Contains(Uv(surface, cells[i] % 16384))
                    )
                        continue;
                    var p = Point(grid, i);
                    union[i] |=
                        PortraitBreakoutPlan.WithinEnvelope(frame, p.X, p.Y)
                        && p.Y >= frame.Bottom + (region.BottomCrop ? frame.Height * 0.08f : 0);
                }
        if (!union.Any(p => p))
            return PortraitBreakoutPlan.Empty(frame);
        return grid with
        {
            Parts = regions.Select(r => r.Part).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            Runs = Runs(union),
            Regions = regions.ToArray(),
        };
    }

    private sealed record Observation(
        PortraitBreakoutRaster Raster,
        PortraitPoint[] Points,
        byte[] Mapped
    );

    private static Observation? Observe(
        Surface surface,
        int index,
        PortraitCompositionInput input,
        PortraitComposition fit,
        PortraitBreakoutPlan grid,
        Dictionary<string, int> ids,
        Dictionary<int, PortraitBreakoutRaster> rasters,
        CancellationToken cancellation
    )
    {
        if (!rasters.TryGetValue(index, out var raster))
        {
            raster = PortraitBreakoutRasterizer.Render(
                input.Pages,
                input.Poses[index],
                fit,
                grid,
                ids,
                cancellation
            );
            if (raster == null)
                return null;
            rasters.Add(index, raster);
        }
        var points = new PortraitPoint[surface.Width * surface.Height];
        var mapped = new byte[points.Length];
        foreach (
            var triangle in input
                .Poses[index]
                .Triangles.Where(t => t.Part == surface.Name && t.Page == surface.Page)
        )
            Map(surface, triangle, fit, points, mapped, cancellation);
        return new Observation(raster, points, mapped);
    }

    private static bool[] CoreFootprint(
        Surface surface,
        PortraitImage page,
        CancellationToken cancellation
    )
    {
        // The denominator includes all captured UV geometry, not just the visible
        // candidate pose: clipping and occlusion must count as unknown, not disappear.
        var core = new bool[surface.Width * surface.Height];
        foreach (var triangle in surface.Geometry)
            VisitUv(surface, triangle, (i, a, b, c) => core[i] = true, cancellation);
        for (var i = 0; i < core.Length; i++)
        {
            var uv = Uv(surface, i);
            var x = Math.Max(0, Math.Min(page.Width - 1, (int)(uv.X * page.Width)));
            var y = Math.Max(0, Math.Min(page.Height - 1, (int)(uv.Y * page.Height)));
            core[i] &= page.Rgba[(y * page.Width + x) * 4 + 3] >= 220;
        }
        return core;
    }

    private static bool FullyObserved(
        Surface surface,
        Observation observation,
        bool[] core,
        PortraitImage reference,
        PortraitBreakoutPlan grid
    )
    {
        var total = 0;
        var observed = 0;
        var matched = 0;
        for (var i = 0; i < core.Length; i++)
        {
            if (!core[i])
                continue;
            total++;
            if (observation.Mapped[i] != 1)
                continue;
            var pixel = Pixel(grid, observation.Points[i]);
            if (pixel < 0 || observation.Raster.Owners[pixel] != surface.Id)
                continue;
            var comparison = Compare(
                reference,
                observation.Raster.Pixels,
                pixel,
                observation.Points[i]
            );
            if (comparison != 0)
                observed++;
            if (comparison > 0)
                matched++;
        }
        return total > 0 && observed >= total * 0.9f && matched >= observed * 0.8f;
    }

    private static void Map(
        Surface s,
        PortraitTriangle t,
        PortraitComposition fit,
        PortraitPoint[] points,
        byte[] mapped,
        CancellationToken cancellation
    )
    {
        VisitUv(
            s,
            t,
            (i, a, b, c) =>
            {
                var p = new PortraitPoint(
                    (a * t.A.X + b * t.B.X + c * t.C.X) * fit.Scale + fit.X,
                    (a * t.A.Y + b * t.B.Y + c * t.C.Y) * fit.Scale + fit.Y
                );
                if (
                    mapped[i] != 0
                    && (
                        (a > 0.0001f && b > 0.0001f && c > 0.0001f)
                        || Math.Abs(points[i].X - p.X) > 0.005f
                        || Math.Abs(points[i].Y - p.Y) > 0.005f
                    )
                )
                    mapped[i] = 2;
                else if (mapped[i] == 0)
                {
                    mapped[i] = 1;
                    points[i] = p;
                }
            },
            cancellation
        );
    }

    private static void VisitUv(
        Surface s,
        PortraitTriangle t,
        Action<int, float, float, float> visit,
        CancellationToken cancellation
    )
    {
        var area = Edge(t.UvA, t.UvB, t.UvC);
        if (Math.Abs(area) < 1e-10f)
            return;
        var left = Math.Max(
            0,
            (int)
                MathF.Floor(
                    (Math.Min(t.UvA.X, Math.Min(t.UvB.X, t.UvC.X)) - s.Uv.Left)
                        * s.Width
                        / s.Uv.Width
                )
        );
        var right = Math.Min(
            s.Width - 1,
            (int)
                MathF.Ceiling(
                    (Math.Max(t.UvA.X, Math.Max(t.UvB.X, t.UvC.X)) - s.Uv.Left)
                        * s.Width
                        / s.Uv.Width
                )
        );
        var bottom = Math.Max(
            0,
            (int)
                MathF.Floor(
                    (Math.Min(t.UvA.Y, Math.Min(t.UvB.Y, t.UvC.Y)) - s.Uv.Bottom)
                        * s.Height
                        / s.Uv.Height
                )
        );
        var top = Math.Min(
            s.Height - 1,
            (int)
                MathF.Ceiling(
                    (Math.Max(t.UvA.Y, Math.Max(t.UvB.Y, t.UvC.Y)) - s.Uv.Bottom)
                        * s.Height
                        / s.Uv.Height
                )
        );
        for (var y = bottom; y <= top; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = left; x <= right; x++)
            {
                var i = y * s.Width + x;
                var uv = Uv(s, i);
                var a = Edge(t.UvB, t.UvC, uv) / area;
                var b = Edge(t.UvC, t.UvA, uv) / area;
                var c = 1 - a - b;
                if (a < -0.00001f || b < -0.00001f || c < -0.00001f)
                    continue;
                visit(i, a, b, c);
            }
        }
    }

    private static int[] Components(bool[] opaque, int width, int height)
    {
        var ids = new int[opaque.Length];
        var queue = new int[opaque.Length];
        var next = 0;
        for (var start = 0; start < opaque.Length; start++)
        {
            if (!opaque[start] || ids[start] != 0)
                continue;
            next++;
            var count = 1;
            queue[0] = start;
            ids[start] = next;
            for (var cursor = 0; cursor < count; cursor++)
            {
                var pixel = queue[cursor];
                if (pixel % width > 0)
                    Visit(pixel - 1);
                if (pixel % width + 1 < width)
                    Visit(pixel + 1);
                if (pixel >= width)
                    Visit(pixel - width);
                if (pixel + width < width * height)
                    Visit(pixel + width);
            }
            void Visit(int pixel)
            {
                if (!opaque[pixel] || ids[pixel] != 0)
                    return;
                ids[pixel] = next;
                queue[count++] = pixel;
            }
        }
        return ids;
    }

    private static List<Cut> CanvasCuts(PortraitImage image, PortraitFrameGeometry frame)
    {
        var cuts = new List<Cut>();
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
            return cuts;
        Scan(true, left, -1);
        Scan(true, right, 1);
        Scan(false, top, 1);
        return cuts;
        bool Opaque(int x, int y) =>
            x >= 0
            && x < image.Width
            && y >= 0
            && y < image.Height
            && image.Rgba[(y * image.Width + x) * 4 + 3] >= 220;
        void Scan(bool vertical, int edge, int outward)
        {
            var minimum = Math.Max(
                6,
                (int)MathF.Ceiling((vertical ? frame.Height : frame.Width) * image.Height * 0.1f)
            );
            var start = 0;
            var extent = vertical ? image.Height : image.Width;
            for (var along = 0; along <= extent; along++)
            {
                var opaque = along < extent;
                for (var d = 0; opaque && d < 3; d++)
                    opaque = vertical
                        ? Opaque(edge - outward * d, along)
                        : Opaque(along, edge - outward * d);
                if (opaque)
                    continue;
                if (along - start >= minimum)
                    cuts.Add(
                        new Cut(
                            vertical,
                            (edge + (outward > 0 ? 1 : 0)) / (float)image.Height,
                            outward,
                            start / (float)image.Height,
                            along / (float)image.Height
                        )
                    );
                start = along + 1;
            }
        }
    }

    private static bool Beyond(Cut cut, PortraitPoint p) =>
        (cut.Vertical ? p.Y : p.X) >= cut.Low
        && (cut.Vertical ? p.Y : p.X) <= cut.High
        && ((cut.Vertical ? p.X : p.Y) - cut.Edge) * cut.Outward > 0;

    private static PortraitPoint Uv(Surface s, int i) =>
        new(
            s.Uv.Left + (i % s.Width + 0.5f) * s.Uv.Width / s.Width,
            s.Uv.Bottom + (i / s.Width + 0.5f) * s.Uv.Height / s.Height
        );

    private static PortraitPoint Point(PortraitBreakoutPlan grid, int pixel) =>
        new(
            grid.Bounds.Left + (pixel % grid.Width + 0.5f) * grid.Bounds.Width / grid.Width,
            grid.Bounds.Bottom + (pixel / grid.Width + 0.5f) * grid.Bounds.Height / grid.Height
        );

    private static int Pixel(PortraitBreakoutPlan grid, PortraitPoint p)
    {
        var x = (int)MathF.Floor((p.X - grid.Bounds.Left) * grid.Width / grid.Bounds.Width);
        var y = (int)MathF.Floor((p.Y - grid.Bounds.Bottom) * grid.Height / grid.Bounds.Height);
        return x < 0 || x >= grid.Width || y < 0 || y >= grid.Height ? -1 : y * grid.Width + x;
    }

    private static int Compare(PortraitImage reference, byte[] rendered, int pixel, PortraitPoint p)
    {
        var cx = (int)MathF.Floor(p.X * reference.Height);
        var cy = (int)MathF.Floor(p.Y * reference.Height);
        var observed = false;
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            var x = cx + dx;
            var y = cy + dy;
            if (x < 0 || x >= reference.Width || y < 0 || y >= reference.Height)
                continue;
            var i = (y * reference.Width + x) * 4;
            if (reference.Rgba[i + 3] < 220)
                continue;
            observed = true;
            var error = 0;
            for (var c = 0; c < 3; c++)
                error += Math.Abs(reference.Rgba[i + c] - rendered[pixel * 4 + c]);
            if (error <= 135)
                return 1;
        }
        return observed ? -1 : 0;
    }

    private static float Edge(PortraitPoint a, PortraitPoint b, PortraitPoint p) =>
        (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static int[] Runs(bool[] mask)
    {
        var runs = new List<int>();
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;
            var start = i;
            while (i + 1 < mask.Length && mask[i + 1])
                i++;
            runs.Add(start);
            runs.Add(i - start + 1);
        }
        return runs.ToArray();
    }
}
