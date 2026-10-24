#nullable enable
using System.Text;

namespace BazaarPlusPlus.Game.Supporters;

/// <summary>
/// A host's supporter attribution: each call names the next supporters of one shuffled rotation
/// (<see cref="BPPSupporterSampler.SampleMany"/>), so successive panel opens show everyone before
/// anyone repeats. The seed fixes the rotation for the process; the entries come from the caller,
/// normally <see cref="BPPSupporterCatalog.GetCurrentEntries"/>.
/// </summary>
public sealed class BPPSupporterAttribution
{
    private readonly object _sync = new();
    private readonly int _shuffleSeed;
    private int _cursor;

    public BPPSupporterAttribution(int shuffleSeed) => _shuffleSeed = shuffleSeed;

    public IReadOnlyList<BPPSupporterSample> Next(
        IReadOnlyList<BPPSupporterEntry> entries,
        int count
    )
    {
        lock (_sync)
        {
            var samples = BPPSupporterSampler.SampleMany(entries, count, _cursor, _shuffleSeed);
            _cursor += count;
            return samples;
        }
    }

    /// <summary>
    /// One TextMeshPro rich-text line for a touch host's single label: the localized "Supported
    /// by" prefix and suffix around the names, each in its tier's color, separated by dots. Empty
    /// when no sample has a name. Names are stripped of rich-text tags so a name cannot restyle
    /// the line.
    /// </summary>
    public static string FormatRichLine(
        IReadOnlyList<BPPSupporterSample> samples,
        string languageCode
    )
    {
        var supporters = (samples ?? Array.Empty<BPPSupporterSample>())
            .Where(sample => sample.HasValue)
            .ToArray();
        if (supporters.Length == 0)
            return string.Empty;
        var line = new StringBuilder();
        line.Append("<color=#")
            .Append(MutedHex)
            .Append('>')
            .Append(BPPSupporterAttributionText.FormatSupportedByPrefix(languageCode))
            .Append("</color> ");
        for (var i = 0; i < supporters.Length; i++)
        {
            if (i > 0)
                line.Append(" <color=#").Append(MutedHex).Append(">·</color> ");
            line.Append("<color=#")
                .Append(TierHex(supporters[i].Tier))
                .Append('>')
                .Append(Plain(supporters[i].Name))
                .Append("</color>");
        }
        var suffix = BPPSupporterAttributionText.FormatSupportedBySuffix(languageCode);
        if (!string.IsNullOrWhiteSpace(suffix))
            line.Append(" <color=#").Append(MutedHex).Append('>').Append(suffix).Append("</color>");
        return line.ToString();
    }

    /// <summary>The attribution's muted label color.</summary>
    public const string MutedHex = "B0A187";

    /// <summary>A supporter tier's name color, the desktop's <c>Colors.SupporterTier*Text</c>.</summary>
    public static string TierHex(int tier) =>
        tier switch
        {
            >= 4 => "FFC733",
            3 => "C7DBFF",
            2 => "FFA857",
            _ => "C7D4E6",
        };

    private static string Plain(string name) => name.Trim().Replace("<", "‹").Replace(">", "›");
}
