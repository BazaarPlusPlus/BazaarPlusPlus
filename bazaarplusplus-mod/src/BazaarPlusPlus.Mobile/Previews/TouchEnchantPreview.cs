using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Mobile.Previews;

/// <summary>
/// Native enchantment effects in the game's item tooltip, a Native Adjustment. Under
/// <c>[EnchantPreview] Mode</c> the live tooltip lists them in its main text, below the passive
/// text, as the desktop does: <c>Always</c> on every eligible item, <c>AutoOnPedestalChoice</c>
/// only while an enchant pedestal is offered and then only the enchantment(s) it applies,
/// <c>Off</c> never (Core's <see cref="EnchantPreviewVisibility"/>). The section is built when the
/// tooltip's contents render (<see cref="Prepare"/>) and consumed by the passive block of that
/// same render (<see cref="TakePending"/>), so nothing carries over to the next card. A tooltip
/// whose main text lists none keeps the collapsed Enchantments switch in the native description
/// scroll (<see cref="TouchPreviewSwitches"/>), which lists each enchantment's effect from the
/// item's tier up (<see cref="Build"/>). Each host's tooltip patches drive it and read the game
/// through <see cref="IEnchantPreviewHost{TTooltip}"/>. Main thread.
/// </summary>
internal sealed class TouchEnchantPreview<TTooltip>
    where TTooltip : class
{
    private const int PrefixSizePercent = 50;
    private const int EffectSizePercent = 46;

    // The line height over the text size, as the desktop's 60% line height over its 46% text.
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
    private List<(string Name, string Label, string Effect)>? _pending;
    private long _inline;
    private int _failures;

    /// <param name="host">The host's read of its tooltip.</param>
    /// <param name="mode">The <c>[EnchantPreview] Mode</c> setting.</param>
    /// <param name="pedestal">The offered choice pedestal, or null when none is offered or it cannot be read.</param>
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

    /// <summary>How many times the Enchantments switch built its effects.</summary>
    public long Rendered { get; private set; }

    /// <summary>How many sections the main text listed.</summary>
    public long Inline => Interlocked.Read(ref _inline);

    /// <summary>
    /// The enchantments the tooltip's main text lists under <c>[EnchantPreview] Mode</c>, in
    /// <see cref="Candidates"/> order, or none: the mode is off, no enchant pedestal is offered
    /// under the automatic mode, or the item takes no other enchantment.
    /// </summary>
    public IReadOnlyList<string> InlineCandidates(TTooltip tooltip)
    {
        var mode = _mode();
        var pedestal = EnchantPreviewVisibility.ReadsPedestal(mode) ? _pedestal() : null;
        if (!EnchantPreviewVisibility.Shows(mode, pedestal))
            return [];
        var restriction = EnchantPreviewVisibility.Restriction(mode, pedestal);
        return _host.Candidates(tooltip).Where(name => Allowed(name, restriction)).ToArray();
    }

    /// <summary>The enchantments the tooltip's item can take besides its own (<see cref="IEnchantPreviewHost{TTooltip}.Candidates"/>).</summary>
    public IReadOnlyList<string> Candidates(TTooltip tooltip) => _host.Candidates(tooltip);

    /// <summary>The contents of a card tooltip start to render: build its main-text section.</summary>
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

    /// <summary>
    /// The passive block of the rendering tooltip, whose text is <paramref name="label"/>: the
    /// section, consumed once, sized for the label's rendered font. The lines keep the desktop's
    /// reduced size only while that still reads at <see cref="TouchMetrics.MinBodySp"/>
    /// (<see cref="TouchMetrics.ReadablePercent"/>).
    /// </summary>
    public string? TakePending(TMP_Text? label)
    {
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

    /// <summary>The render finished, whether or not the passive block consumed the section.</summary>
    public void Clear() => _pending = null;

    /// <summary>
    /// Each of <paramref name="candidates"/> with its effect on the tooltip's item from its tier
    /// up, the changing numbers merged (<see cref="CollectionTierTooltipTextMerger"/>) and the name
    /// in the enchantment's colour; one the game renders at no tier is left out.
    /// </summary>
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

    // The desktop's ItemEnchantPreviewCandidateSelector: a pedestal that names its enchantment(s)
    // keeps only those; an empty or absent restriction keeps every one.
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

    // The label's font size in screen pixels: its canvas units, scaled by its transform within
    // the canvas and by the canvas's own scale factor. Zero when it cannot be read.
    private static float RenderedFontPixels(TMP_Text? label)
    {
        var port = MobilePlatform.Port;
        if (!port.IsAlive(label))
            return 0f;
        var parent = label!.canvas;
        var canvas = port.IsAlive(parent) ? parent.rootCanvas : null;
        if (!port.IsAlive(canvas))
            return 0f;
        var canvasScale = canvas!.transform.lossyScale.y;
        var relative = canvasScale > 0f ? label.transform.lossyScale.y / canvasScale : 1f;
        return label.fontSize * relative * canvas.scaleFactor;
    }
}
