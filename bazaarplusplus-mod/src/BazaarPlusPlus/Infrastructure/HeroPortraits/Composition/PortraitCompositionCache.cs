#nullable enable
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

internal sealed class PortraitCompositionCache
{
    // Bump when capture conventions, feature descriptors, or fitting rules change.
    internal const int AlgorithmVersion = 3;
    private const int AtlasOnlyVersion = 2;
    private readonly string _directory;

    internal PortraitCompositionCache(string directory) => _directory = directory;

    internal PortraitComposition? FindOrCompute(
        PortraitCompositionInput input,
        CancellationToken cancellation
    ) => FindCached(input, cancellation, out var key) ?? Compute(input, key, cancellation);

    internal PortraitComposition? FindCached(
        PortraitCompositionInput input,
        CancellationToken cancellation,
        out string key
    )
    {
        key = Fingerprint(input, cancellation);
        var path = Path.Combine(_directory, key + ".json");
        var fit = Read(path, input);
        if (fit != null)
            return fit;
        // Version 3 adds a fallback without changing accepted atlas fits. Keep the user's
        // already validated positive results, then promote them to the current cache key.
        var previousPath = Path.Combine(
            _directory,
            Fingerprint(input, cancellation, AtlasOnlyVersion) + ".json"
        );
        fit = Read(previousPath, input);
        cancellation.ThrowIfCancellationRequested();
        if (fit == null)
            return null;
        Write(path, fit);
        return fit;
    }

    internal PortraitComposition? Compute(
        PortraitCompositionInput input,
        string key,
        CancellationToken cancellation
    )
    {
        var fit = PortraitCompositionMatcher.Match(input, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (fit != null)
            Write(Path.Combine(_directory, key + ".json"), fit);
        return fit;
    }

    private static void Write(string path, PortraitComposition fit)
    {
        try
        {
            // Only numeric results are written; no game artwork or skeletons leave memory.
            AtomicFileWriter.Write(
                path,
                temp => File.WriteAllText(temp, JsonConvert.SerializeObject(fit))
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only cache still permits this session's in-memory result.
        }
    }

    private static PortraitComposition? Read(string path, PortraitCompositionInput input)
    {
        try
        {
            if (File.Exists(path))
            {
                var fit = JsonConvert.DeserializeObject<PortraitComposition>(
                    File.ReadAllText(path)
                );
                if (fit != null && PortraitCompositionMatcher.IsApplicable(input, fit))
                    return fit;
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Cache damage must never prevent recalculation or reuse of a prior valid fit.
        }
        return null;
    }

    internal static string Fingerprint(
        PortraitCompositionInput input,
        CancellationToken cancellation,
        int algorithmVersion = AlgorithmVersion
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(algorithmVersion);
            writer.Write(input.Pages.Length);
            foreach (var image in new[] { input.Reference }.Concat(input.Pages))
            {
                writer.Write(image.Width);
                writer.Write(image.Height);
                writer.Write(image.Rgba.Length);
                writer.Write(image.Rgba);
            }
            writer.Write(input.Poses.Length);
            foreach (var pose in input.Poses)
            {
                cancellation.ThrowIfCancellationRequested();
                writer.Write(pose.Time);
                writer.Write(pose.Triangles.Length);
                foreach (var t in pose.Triangles)
                {
                    writer.Write(t.Page);
                    writer.Write(t.A.X);
                    writer.Write(t.A.Y);
                    writer.Write(t.B.X);
                    writer.Write(t.B.Y);
                    writer.Write(t.C.X);
                    writer.Write(t.C.Y);
                    writer.Write(t.UvA.X);
                    writer.Write(t.UvA.Y);
                    writer.Write(t.UvB.X);
                    writer.Write(t.UvB.Y);
                    writer.Write(t.UvC.X);
                    writer.Write(t.UvC.Y);
                }
            }
        }
        stream.Position = 0;
        using var hash = SHA256.Create();
        var text = new StringBuilder(64);
        foreach (var value in hash.ComputeHash(stream))
            text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
