#nullable enable
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

// Card prefab references (_smallItemReference / _mediumItemReference / etc.) were removed
// from MonsterBoardTooltip in a game update. We now create card instances via
// AssetLoader.InstantiateUICardAsync instead of cloning serialised prefabs.
// The _sockets field still exists and is still captured here for socket-layout sizing.
internal static class NativeCardPreviewPrefabResolver
{
    private const string MonsterBoardTooltipTypeName = "TheBazaar.UI.Tooltips.MonsterBoardTooltip";

    private static readonly object Lock = new();
    private static NativeCardPreviewSocketTemplate[]? _socketTemplates;
    private static bool _socketsResolved;

    private static readonly Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        MonsterBoardTooltipTypeName
    );

    private static readonly FieldInfo? SocketsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_sockets")
            : null;

    public static NativeCardPreviewSocketTemplate[]? TryGetSocketTemplates()
    {
        lock (Lock)
            return _socketsResolved ? _socketTemplates : null;
    }

    // Returns true when all required resources are available:
    //   • AssetLoader service is reachable (always required for card creation)
    //   • If requireSockets: socket templates captured from MonsterBoardTooltip._sockets
    // requireSkill is kept for API symmetry; AssetLoader handles all card kinds uniformly.
    public static bool TryEnsureResolved(
        bool requireSkill,
        bool requireSockets,
        string logComponent
    )
    {
        var assetLoader = Services.Get<AssetLoader>();
        if (assetLoader == null)
        {
            BppLog.Warn(
                logComponent,
                "AssetLoader service unavailable; native card preview unavailable on this game version."
            );
            return false;
        }

        if (!requireSockets)
            return true;

        lock (Lock)
        {
            if (_socketsResolved)
                return true;
        }

        return TryCaptureSocketTemplates(logComponent);
    }

    // Creates a card via AssetLoader.InstantiateUICardAsync and returns its CardPreviewBase
    // component. On pool-miss the caller receives a fully-instantiated (and initially SetUp)
    // card; the caller then calls InvokeSetUpSafe again to bind it to the target card data.
    public static async Task<Component?> TryCreateCardAsync(
        TCardInstance instance,
        Transform? parent,
        string logComponent
    )
    {
        try
        {
            var assetLoader = Services.Get<AssetLoader>();
            if (assetLoader == null)
            {
                BppLog.Warn(logComponent, "AssetLoader unavailable for card creation.");
                return null;
            }

            var go = await assetLoader.InstantiateUICardAsync(instance, parent, CancellationToken.None);
            if (go == null)
            {
                BppLog.Warn(logComponent, "AssetLoader.InstantiateUICardAsync returned null.");
                return null;
            }

            var cardPreviewBaseType = NativeCardPreviewReflection.CardPreviewBaseType;
            if (cardPreviewBaseType == null)
            {
                BppLog.Warn(logComponent, "CardPreviewBaseType is null — type lookup failed.");
                return null;
            }

            var component = go.GetComponent(cardPreviewBaseType);
            if (component == null)
                BppLog.Warn(logComponent, $"GetComponent({cardPreviewBaseType.Name}) returned null on GO '{go.name}'.");
            return component;
        }
        catch (Exception ex)
        {
            BppLog.Warn(logComponent, $"Card creation via AssetLoader failed: {ex.Message}");
            return null;
        }
    }

    private static bool TryCaptureSocketTemplates(string logComponent)
    {
        if (MonsterBoardTooltipType == null || SocketsField == null)
        {
            BppLog.Warn(
                logComponent,
                $"{MonsterBoardTooltipTypeName}._sockets reflection missing; socket layout will use fallback sizing."
            );
            return false;
        }

        var tooltips = Resources.FindObjectsOfTypeAll(MonsterBoardTooltipType);
        if (tooltips == null || tooltips.Length == 0)
            return false;

        foreach (var tooltipObj in tooltips)
        {
            if (tooltipObj is not Component tooltip || tooltip == null)
                continue;

            var sockets = SocketsField.GetValue(tooltip) as RectTransform[];
            if (sockets == null || sockets.Length == 0)
                continue;

            lock (Lock)
            {
                _socketTemplates = CaptureSocketTemplates(sockets);
                _socketsResolved = true;
            }

            BppLog.Info(
                logComponent,
                $"Acquired socket templates from {MonsterBoardTooltipTypeName} (sockets={sockets.Length})."
            );
            return true;
        }

        return false;
    }

    private static NativeCardPreviewSocketTemplate[] CaptureSocketTemplates(RectTransform[] sockets)
    {
        var templates = new NativeCardPreviewSocketTemplate[sockets.Length];
        for (var i = 0; i < sockets.Length; i++)
        {
            var socket = sockets[i];
            if (socket == null)
            {
                templates[i] = new NativeCardPreviewSocketTemplate(
                    Vector2.zero,
                    new Vector2(160f, 220f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f)
                );
                continue;
            }

            templates[i] = new NativeCardPreviewSocketTemplate(
                socket.anchoredPosition,
                socket.sizeDelta,
                socket.anchorMin,
                socket.anchorMax,
                socket.pivot
            );
        }
        return templates;
    }
}
