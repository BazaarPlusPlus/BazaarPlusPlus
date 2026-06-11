#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal static class BppOverlayPanelMutex
{
    private const string LogCategory = "OverlayPanelMutex";
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IBppOverlayPanel> Panels = new(
        StringComparer.Ordinal
    );

    public static void Register(IBppOverlayPanel panel)
    {
        if (panel == null)
            throw new ArgumentNullException(nameof(panel));

        lock (SyncRoot)
            Panels[panel.PanelId] = panel;
    }

    public static void Unregister(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            return;

        lock (SyncRoot)
            Panels.Remove(panelId);
    }

    public static void CloseOthers(string activePanelId, int sortingBand)
    {
        List<IBppOverlayPanel> targets;
        lock (SyncRoot)
        {
            targets = Panels
                .Values.Where(panel =>
                    panel.SortingBand == sortingBand
                    && !string.Equals(panel.PanelId, activePanelId, StringComparison.Ordinal)
                    && panel.IsVisible
                )
                .ToList();
        }

        foreach (var panel in targets)
        {
            try
            {
                panel.Close();
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    LogCategory,
                    $"Failed to close panel '{panel.PanelId}' for active panel '{activePanelId}': {ex.Message}"
                );
            }
        }
    }
}
