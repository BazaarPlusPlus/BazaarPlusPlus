#nullable enable
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal sealed class PortraitBreakoutCache
{
    private const int Version = 12;
    private readonly string _directory;

    internal PortraitBreakoutCache(string directory) =>
        _directory = Path.Combine(directory, "Breakout");

    internal (PortraitBreakoutPlan Plan, bool Cached) FindOrCompute(
        PortraitCompositionInput input,
        PortraitComposition fit,
        PortraitFrameGeometry frame,
        string source,
        CancellationToken cancellation
    )
    {
        var key = Fingerprint(input, fit, frame, source, cancellation);
        var path = Path.Combine(_directory, key + ".json");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 131072)
            {
                var cached = JsonConvert.DeserializeObject<PortraitBreakoutPlan>(
                    File.ReadAllText(path)
                );
                if (cached?.IsValid(frame) == true)
                    return (cached, true);
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        var plan = PortraitBreakoutAnalyzer.Analyze(input, fit, frame, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!plan.IsValid(frame))
            return (PortraitBreakoutPlan.Empty(frame), false);
        try
        {
            AtomicFileWriter.Write(
                path,
                temp => File.WriteAllText(temp, JsonConvert.SerializeObject(plan))
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (plan, false);
    }

    private static string Fingerprint(
        PortraitCompositionInput input,
        PortraitComposition fit,
        PortraitFrameGeometry frame,
        string source,
        CancellationToken cancellation
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);
            writer.Write(source);
            writer.Write(PortraitCompositionCache.Fingerprint(input, cancellation));
            writer.Write(JsonConvert.SerializeObject(fit));
            writer.Write(JsonConvert.SerializeObject(frame));
            foreach (var pose in input.Poses)
            {
                cancellation.ThrowIfCancellationRequested();
                writer.Write(pose.BreakoutSupported);
                foreach (var item in pose.Triangles.GroupBy(t => t.Part).Select(g => g.First()))
                {
                    writer.Write(item.Part ?? "");
                    var influences = item.Rig?.Influences ?? [];
                    writer.Write(influences.Length);
                    foreach (var influence in influences)
                    {
                        writer.Write(influence.Weight);
                        writer.Write(influence.Ancestors.Length);
                        foreach (var bone in influence.Ancestors)
                            writer.Write(bone);
                    }
                }
                var order = pose.DrawOrder ?? [];
                writer.Write(order.Length);
                foreach (var part in order)
                    writer.Write(part);
                foreach (
                    var item in pose
                        .Triangles.Select(t => (t.Part, t.Opacity, t.BreakoutEligible))
                        .Distinct()
                )
                {
                    writer.Write(item.Part ?? "");
                    writer.Write(item.Opacity);
                    writer.Write(item.BreakoutEligible);
                }
            }
        }
        using var hash = SHA256.Create();
        return BitConverter
            .ToString(hash.ComputeHash(stream.ToArray()))
            .Replace("-", "")
            .ToLowerInvariant();
    }
}
