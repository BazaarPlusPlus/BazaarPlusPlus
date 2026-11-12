#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

/// <summary>One cell of a recommendation's evidence: its label and its formatted value.</summary>
public readonly record struct LiveBuildMetric(string Label, string Value);
