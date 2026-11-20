using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Previews;

// Prepare and TakePending share one tooltip render. Discard unconsumed text so it
// cannot leak into the next card's passive block.
internal sealed class TouchEnchantPreview<TTooltip>
    where TTooltip : class
{
    private const int PrefixSizePercent = 50;
    private const int EffectSizePercent = 46;

    private const float LineHeightOverSize = 1.3f;
    private const long LoggedInline = 10;
    private const int LoggedFailures = 5;

    private static readonly Regex NativeLineHeight = new(
        "</?line-height[^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private readonly IEnchantPreviewHost<TTooltip> _host;
    private readonly Func<PreviewVisibilityMode> _mode;
    private readonly Func<ChoicePedestalSnapshot?> _pedestal;

    private readonly Dictionary<int, float> _floors = new();
    private List<(string Name, string Label, string Effect)>? _pending;
    private long _inline;
    private int _failures;

    public TouchEnchantPreview(
        IEnchantPreviewHost<TTooltip> host,
        Func<PreviewVisibilityMode> mode,
        Func<ChoicePedestalSnapshot?> pedestal
    )
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _pedestal = pedestal ?? throw new ArgumentNullException(nameof(pedestal));
    }

    public long Rendered { get; private set; }

    public long Inline => Interlocked.Read(ref _inline);

    public IReadOnlyList<string> InlineCandidates(TTooltip tooltip)
    {
        if (!MobilePlatform.Port.PlayState().OffersEnchantments)
            return [];
        var mode = _mode();
        var pedestal = EnchantPreviewVisibility.ReadsPedestal(mode) ? _pedestal() : null;
        if (!EnchantPreviewVisibility.Shows(mode, pedestal))
            return [];
        var restriction = EnchantPreviewVisibility.Restriction(mode, pedestal);
        return _host.Candidates(tooltip).Where(name => Allowed(name, restriction)).ToArray();
    }

    public IReadOnlyList<string> Candidates(TTooltip tooltip) =>
        MobilePlatform.Port.PlayState().OffersEnchantments ? _host.Candidates(tooltip) : [];

    public IReadOnlyList<string> SwitchCandidates(TTooltip tooltip) =>
        InlineCandidates(tooltip).Count > 0 ? [] : Candidates(tooltip);

    public void Prepare(TTooltip? tooltip)
    {
        _pending = null;
        if (tooltip == null)
            return;
        try
        {
            // The upgrade preview wins over the enchant preview, as on the desktop.
            if (_host.HoldsUpgrade(tooltip))
                return;
            var candidates = InlineCandidates(tooltip);
            if (candidates.Count == 0)
                return;
            var lines = new List<(string Name, string Label, string Effect)>();
            foreach (var name in candidates)
            {
                var effect = _host.Effect(tooltip, name);
                if (!string.IsNullOrWhiteSpace(effect))
                    lines.Add((name, _host.Label(name), effect!.Replace('\n', ' ')));
            }
            _pending = lines.Count == 0 ? null : lines;
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref _failures) <= LoggedFailures)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Touch,
                        "touch.enchant_preview.degraded",
                        storm: ["reason_code"]
                    ),
                    exception,
                    ("reason_code", "build_exception")
                );
        }
    }

    public string? TakePending(TMP_Text? label)
    {
        RestoreFloor(label);
        var lines = _pending;
        _pending = null;
        if (lines == null)
            return null;
        var fontPixels = RenderedFontPixels(label);
        var dpi = UnityEngine.Screen.dpi;
        var effect = TouchMetrics.ReadablePercent(EffectSizePercent, fontPixels, dpi);
        var prefix = Math.Max(PrefixSizePercent, effect);
        // A short blank first line keeps the list off the block's top divider.
        var section =
            "<size=40%> </size>\n"
            + string.Join("\n", lines.Select(line => Line(line, prefix, effect)));
        var count = Interlocked.Increment(ref _inline);
        if (count <= LoggedInline)
        {
            var pixelsPerSp = TouchMetrics.EffectiveDpi(dpi) / TouchMetrics.BaselineDpi;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.enchant_preview.rendered"),
                ("count", count),
                ("lines", lines.Count),
                ("length", section.Length),
                ("font_px", Math.Round(fontPixels, 1)),
                ("effect_percent", effect),
                ("effect_sp", Math.Round(fontPixels * effect / 100f / pixelsPerSp, 1))
            );
        }
        return section;
    }

    // The game's TMPTrueHeight sums glyph heights and misses line and paragraph spacing.
    // Raise the passive block's minimum height to prevent overlap with adjacent sections.
    public void Fit(TMP_Text? label)
    {
        if (!Il2CppUi.IsAlive(label))
            return;
        var layout = label.GetComponent<LayoutElement>();
        if (layout == null)
            return;
        label.ForceMeshUpdate();
        var id = layout.GetInstanceID();
        if (!_floors.TryGetValue(id, out var floor))
            _floors[id] = floor = layout.minHeight;
        layout.minHeight = Math.Max(floor, label.renderedHeight);
    }

    public void Clear() => _pending = null;

    private void RestoreFloor(TMP_Text? label)
    {
        if (_floors.Count == 0 || !Il2CppUi.IsAlive(label))
            return;
        var layout = label.GetComponent<LayoutElement>();
        if (layout != null && _floors.Remove(layout.GetInstanceID(), out var floor))
            layout.minHeight = floor;
    }

    public IReadOnlyList<(string Label, string Text)> Build(
        TTooltip tooltip,
        IReadOnlyList<string> candidates
    )
    {
        var lines = new List<(string, string)>();
        foreach (var name in candidates)
        {
            var effects = _host.TierEffects(tooltip, name);
            if (effects.Count > 0)
                lines.Add(
                    (
                        $"<color=#{ItemEnchantPreviewText.ColorHex(name)}>{_host.Label(name)}</color>",
                        CollectionTierTooltipTextMerger.MergeLines(
                            effects,
                            keepValuesTogether: true
                        )
                    )
                );
        }
        Rendered++;
        return lines;
    }

    private static bool Allowed(string name, IReadOnlyList<string>? restriction)
    {
        if (restriction == null || restriction.Count == 0)
            return true;
        foreach (var allowed in restriction)
        {
            if (string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Line(
        (string Name, string Label, string Effect) line,
        int prefixPercent,
        int effectPercent
    )
    {
        // The game's rendered text sets its own line height for full-size text; the preview's
        // smaller lines replace it so that the whole list fits a phone-sized tooltip.
        var scaled = ItemEnchantPreviewText.ScaleInlineSizes(
            NativeLineHeight.Replace(line.Effect, ""),
            effectPercent / 100f
        );
        var lineHeight = (int)Math.Round(effectPercent * LineHeightOverSize);
        return $"<line-height={lineHeight}%><size={prefixPercent}%><color=#{ItemEnchantPreviewText.ColorHex(line.Name)}>{line.Label}</color>: </size>"
            + $"<size={effectPercent}%>{scaled}</size></line-height>";
    }

    private static float RenderedFontPixels(TMP_Text? label)
    {
        if (!Il2CppUi.IsAlive(label))
            return 0f;
        var parent = label.canvas;
        var canvas = Il2CppUi.IsAlive(parent) ? parent.rootCanvas : null;
        if (!Il2CppUi.IsAlive(canvas))
            return 0f;
        var canvasScale = canvas.transform.lossyScale.y;
        var relative = canvasScale > 0f ? label.transform.lossyScale.y / canvasScale : 1f;
        return label.fontSize * relative * canvas.scaleFactor;
    }
}
