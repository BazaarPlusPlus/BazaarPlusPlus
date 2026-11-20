#nullable enable
using System.Text;

namespace BazaarPlusPlus.Game.Supporters;

// A process-wide shuffled rotation gives every supporter a turn before any repeats.
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

    // Strip rich-text tags from submitted names so they cannot restyle the TextMeshPro label.
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

    // This value mirrors the ingame-ink-muted design token.
    public const string MutedHex = "B0A187";

    // The design check pins these copies to the ingame-supporter-* tokens because Core cannot read the generated theme.
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
