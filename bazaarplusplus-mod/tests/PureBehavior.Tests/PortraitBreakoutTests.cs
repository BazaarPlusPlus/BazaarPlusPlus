using BazaarPlusPlus.Game.HeroPortraits;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using Xunit;

namespace PureBehavior.Tests;

public sealed class PortraitBreakoutTests
{
    private static readonly PortraitFrameGeometry Frame = new(0, 0, 1, 1);
    private static readonly PortraitComposition Fit = new(1, 0, 0, 0, 20, 0, 0.5f, 0.5f);

    [Theory]
    [InlineData(1f)]
    [InlineData(0.35f)]
    public void A_verified_prop_keeps_its_canvas_cropped_tip_through_motion_and_fading(
        float opacity
    )
    {
        var input = Prop(opacity);
        var plan = Analyze(input);

        Assert.Equal(new[] { "0:handle", "1:tip" }, plan.Parts);
        Assert.True(plan.IsValid(Frame));
        Assert.Equal(1f, plan.ContentScale);
        Assert.True(Admits(plan, 0.5f, 1.08f));
        Assert.True(Admits(plan, 0.5f, 1.18f));
        Assert.True(Admits(plan, 0.6f, 1.18f));
        Assert.False(Admits(plan, 0.9f, 1.4f));
    }

    [Fact]
    public void Artwork_that_contradicts_the_reference_cannot_borrow_a_prop_anchor()
    {
        var input = Prop();
        for (var i = 0; i < input.Reference.Rgba.Length; i += 4)
            if (input.Reference.Rgba[i + 3] != 0)
            {
                input.Reference.Rgba[i] = 255;
                input.Reference.Rgba[i + 1] = 0;
                input.Reference.Rgba[i + 2] = 0;
            }

        Assert.Empty(Analyze(input).Parts);
    }

    [Fact]
    public void An_oversized_later_pose_rejects_the_whole_prop()
    {
        var input = Prop();
        input.Poses[1] = new PortraitPose(
            1,
            [
                .. Quad("0:handle", 0.46f, 0.65f, 0.54f, 1.12f),
                .. Quad("1:tip", 0.2f, 1.05f, 0.8f, 1.55f),
            ]
        );

        var plan = Analyze(input);

        Assert.Empty(plan.Parts);
        Assert.True(plan.IsValid(Frame));
    }

    [Fact]
    public void Unsupported_capture_falls_back_to_the_strict_frame()
    {
        var input = Prop();
        input.Poses[1] = input.Poses[1] with { BreakoutSupported = false };

        var plan = Analyze(input);

        Assert.Empty(plan.Parts);
        Assert.Empty(plan.Runs);
        Assert.Equal(Fit, plan.Apply(Fit));
    }

    [Theory]
    [InlineData(0.35f)]
    [InlineData(1f)]
    public void Coverage_preserves_surfaces_under_translucent_or_opaque_overlays(float opacity)
    {
        var grid = new PortraitBreakoutPlan(Frame, Frame, 32, 32, [], []);
        var pose = new PortraitPose(
            0,
            [
                .. Quad("base", 0.2f, 0.2f, 0.8f, 0.8f),
                .. Quad("overlay", 0.2f, 0.2f, 0.8f, 0.8f, opacity, eligible: false),
            ]
        );
        var raster = PortraitBreakoutRasterizer.Render(
            [Page()],
            pose,
            Fit,
            grid,
            new() { ["base"] = 0, ["overlay"] = 1 },
            CancellationToken.None,
            captureCoverage: true
        );

        Assert.NotNull(raster);
        const int center = 16 * 32 + 16;
        Assert.Contains(center, Assert.Single(raster.Coverage, p => p.Part == 0).Pixels);
        Assert.Contains(center, Assert.Single(raster.Coverage, p => p.Part == 1).Pixels);
        Assert.True(raster.Owners[center] < 0);
    }

    [Fact]
    public void Renderer_limits_remove_complete_props_without_reordering_surfaces()
    {
        var grid = new PortraitBreakoutPlan(Frame, new(-0.4f, 0, 1.4f, 1.6f), 128, 128, [], []);
        var pixels = Enumerable
            .Range(94, 3)
            .SelectMany(y => Enumerable.Range(63, 3).Select(x => y * grid.Width + x))
            .ToArray();
        var parts = Enumerable
            .Range(0, 28)
            .Select(i => new PortraitBreakoutPart($"part:{i}", null, 1)
            {
                ReferenceMatches = 30,
                Exterior = new bool[grid.Width * grid.Height],
            })
            .ToArray();
        var groups = Enumerable.Range(0, 14).Select(i => new[] { i * 2, i * 2 + 1 }).ToArray();
        var coverage = parts
            .Select((_, i) => new PortraitBreakoutCoverage(i, pixels, false))
            .ToArray();
        var order = Enumerable.Range(0, parts.Length).SelectMany(i => new[] { i, -1 }).ToArray();
        var originalOrder = order.ToArray();

        var plan = PortraitBreakoutSelection.Select(
            grid,
            parts,
            groups,
            [coverage],
            [order],
            CancellationToken.None
        );

        Assert.NotEmpty(plan.Parts);
        Assert.True(plan.Parts.Length < parts.Length);
        Assert.True(plan.IsValid(Frame));
        foreach (var group in groups)
            Assert.Equal(
                plan.Parts.Contains(parts[group[0]].Name),
                plan.Parts.Contains(parts[group[1]].Name)
            );
        var allowed = order.Select(i => i >= 0 && plan.Parts.Contains(parts[i].Name)).ToArray();
        var segments = 1 + allowed.Zip(allowed.Skip(1)).Count(pair => pair.First != pair.Second);
        Assert.InRange(segments, 1, PortraitBreakoutPlan.MaxParts);
        Assert.Equal(originalOrder, order);
    }

    [Fact]
    public void A_new_cache_instance_reuses_the_numeric_plan_and_recovers_from_corrupt_json()
    {
        using var directory = new CacheDirectory();
        var input = Prop();
        var first = new PortraitBreakoutCache(directory.Path).FindOrCompute(
            input,
            Fit,
            Frame,
            "skin-v1",
            CancellationToken.None
        );
        var hit = new PortraitBreakoutCache(directory.Path).FindOrCompute(
            input,
            Fit,
            Frame,
            "skin-v1",
            CancellationToken.None
        );

        Assert.False(first.Cached);
        Assert.NotEmpty(first.Plan.Parts);
        Assert.True(hit.Cached);
        Assert.Equal(first.Plan.Parts, hit.Plan.Parts);
        Assert.Equal(first.Plan.Runs, hit.Plan.Runs);
        Assert.Equal(first.Plan.Apply(Fit), hit.Plan.Apply(Fit));

        File.WriteAllText(
            Assert.Single(
                Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories)
            ),
            "{broken"
        );
        var recovered = new PortraitBreakoutCache(directory.Path).FindOrCompute(
            input,
            Fit,
            Frame,
            "skin-v1",
            CancellationToken.None
        );
        Assert.False(recovered.Cached);
        Assert.Equal(first.Plan.Parts, recovered.Plan.Parts);
        Assert.Equal(first.Plan.Runs, recovered.Plan.Runs);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("frame")]
    [InlineData("fit")]
    [InlineData("artwork")]
    [InlineData("geometry")]
    [InlineData("opacity")]
    [InlineData("eligibility")]
    [InlineData("rig")]
    [InlineData("draw-order")]
    public void Changes_to_rendering_inputs_invalidate_the_cached_plan(string change)
    {
        using var directory = new CacheDirectory();
        var input = Prop();
        var cache = new PortraitBreakoutCache(directory.Path);
        cache.FindOrCompute(input, Fit, Frame, "skin-v1", CancellationToken.None);
        var source = change == "source" ? "skin-v2" : "skin-v1";
        var frame = change == "frame" ? Frame with { Right = 0.95f } : Frame;
        var fit = change == "fit" ? Fit with { X = 0.01f } : Fit;
        if (change == "artwork")
            input.Pages[0].Rgba[0]++;
        if (change == "draw-order")
            input.Poses[0] = input.Poses[0] with { DrawOrder = ["1:tip", "0:handle"] };
        for (var i = 0; i < input.Poses[0].Triangles.Length; i++)
        {
            var triangle = input.Poses[0].Triangles[i];
            input.Poses[0].Triangles[i] = change switch
            {
                "geometry" => triangle with { A = triangle.A with { X = triangle.A.X + 0.01f } },
                "opacity" => triangle with { Opacity = 0.5f },
                "eligibility" => triangle with { BreakoutEligible = false },
                "rig" => triangle with { Rig = new([new(1, [2, 0])]) },
                _ => triangle,
            };
        }

        Assert.False(cache.FindOrCompute(input, fit, frame, source, CancellationToken.None).Cached);
    }

    [Fact]
    public void Cancellation_does_not_publish_a_partial_plan_and_a_retry_can_succeed()
    {
        using var directory = new CacheDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cache = new PortraitBreakoutCache(directory.Path);
        var input = Prop();

        Assert.Throws<OperationCanceledException>(() =>
            cache.FindOrCompute(input, Fit, Frame, "skin-v1", cancellation.Token)
        );
        Assert.Empty(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories));
        var retry = cache.FindOrCompute(input, Fit, Frame, "skin-v1", CancellationToken.None);
        Assert.False(retry.Cached);
        Assert.NotEmpty(retry.Plan.Parts);
    }

    private static PortraitBreakoutPlan Analyze(PortraitCompositionInput input) =>
        PortraitBreakoutAnalyzer.Analyze(input, Fit, Frame, CancellationToken.None);

    private static PortraitCompositionInput Prop(float laterOpacity = 1)
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            if ((x + 0.5f) / size is >= 0.46f and <= 0.54f && (y + 0.5f) / size >= 0.65f)
            {
                var index = (y * size + x) * 4;
                pixels[index] = 70;
                pixels[index + 1] = 120;
                pixels[index + 2] = 160;
                pixels[index + 3] = 255;
            }
        return new(
            new(size, size, pixels),
            [Page()],
            [
                new(
                    0,
                    [
                        .. Quad("0:handle", 0.46f, 0.65f, 0.54f, 1.12f),
                        .. Quad("1:tip", 0.43f, 1.12f, 0.57f, 1.23f),
                    ],
                    DrawOrder: ["0:handle", "1:tip"]
                ),
                new(
                    1,
                    [
                        .. Quad(
                            "0:handle",
                            0.56f,
                            0.65f,
                            0.64f,
                            1.12f,
                            laterOpacity,
                            laterOpacity == 1
                        ),
                        .. Quad(
                            "1:tip",
                            0.53f,
                            1.12f,
                            0.67f,
                            1.23f,
                            laterOpacity,
                            laterOpacity == 1
                        ),
                    ],
                    DrawOrder: ["0:handle", "1:tip"]
                ),
            ]
        );
    }

    private static PortraitImage Page() =>
        new(2, 2, [70, 120, 160, 255, 70, 120, 160, 255, 70, 120, 160, 255, 70, 120, 160, 255]);

    private static PortraitTriangle[] Quad(
        string part,
        float left,
        float bottom,
        float right,
        float top,
        float opacity = 1,
        bool eligible = true
    )
    {
        PortraitPoint a = new(left, bottom),
            b = new(right, bottom),
            c = new(right, top),
            d = new(left, top);
        PortraitRigBinding rig = new([new(1, [1, 0])]);
        return
        [
            new(0, a, b, c, new(0, 0), new(1, 0), new(1, 1), part, opacity, eligible, rig),
            new(0, c, d, a, new(1, 1), new(0, 1), new(0, 0), part, opacity, eligible, rig),
        ];
    }

    private static bool Admits(PortraitBreakoutPlan plan, float x, float y)
    {
        var column = (int)((x - plan.Bounds.Left) * plan.Width / plan.Bounds.Width);
        var row = (int)((y - plan.Bounds.Bottom) * plan.Height / plan.Bounds.Height);
        var pixel = row * plan.Width + column;
        return Enumerable
            .Range(0, plan.Runs.Length / 2)
            .Any(i => pixel >= plan.Runs[i * 2] && pixel < plan.Runs[i * 2] + plan.Runs[i * 2 + 1]);
    }

    private sealed class CacheDirectory : IDisposable
    {
        internal string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "bpp-breakout-tests-" + Guid.NewGuid()
            );

        internal CacheDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
