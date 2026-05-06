#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal static class LegendaryPositionDisplayFormatter
{
    private static IBppConfig? _config;

    public static void Install(IBppConfig config) =>
        _config = config ?? throw new ArgumentNullException(nameof(config));

    private static IBppConfig Config =>
        _config
        ?? throw new InvalidOperationException(
            "LegendaryPositionDisplayFormatter.Install must be called at startup."
        );

    internal static string Format(string? currentText, int? fallbackPosition)
    {
        var mode =
            Config.LegendaryPositionDisplayModeConfig?.Value
            ?? LegendaryPositionDisplayMode.Default;

        return mode switch
        {
            LegendaryPositionDisplayMode.Blank => string.Empty,
            LegendaryPositionDisplayMode.Fixed999999 => "999999",
            LegendaryPositionDisplayMode.PositionWithRating => FormatPositionWithRating(
                currentText,
                fallbackPosition
            ),
            _ => currentText ?? fallbackPosition?.ToString() ?? string.Empty,
        };
    }

    private static string FormatPositionWithRating(string? currentText, int? fallbackPosition)
    {
        var position = ResolvePosition(fallbackPosition);
        BppClientCacheBridge.TryGetPlayerRankSnapshot(out _, out var rating, out _);
        if (!position.HasValue || !rating.HasValue)
            return currentText ?? fallbackPosition?.ToString() ?? string.Empty;

        return $"#{position.Value} | {rating.Value}";
    }

    private static int? ResolvePosition(int? fallbackPosition)
    {
        return
            BppClientCacheBridge.TryGetPlayerLeaderboardPosition(out var cachedPosition)
            && cachedPosition.HasValue
            ? cachedPosition
            : fallbackPosition;
    }
}
