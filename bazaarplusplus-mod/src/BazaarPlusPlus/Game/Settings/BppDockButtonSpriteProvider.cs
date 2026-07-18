#nullable enable
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppDockButtonSpriteProvider
{
    private static readonly Dictionary<BppDockButtonSpriteId, Sprite?> CachedSprites = [];

    internal static Sprite? Get(
        BppDockButtonSpriteId spriteId = BppDockButtonSpriteId.CollectionPanel
    )
    {
        if (CachedSprites.TryGetValue(spriteId, out var cachedSprite))
            return cachedSprite;

        var spec = Resolve(spriteId);
        var sprite = LoadSprite(spec.ResourceSuffix, spec.SpriteName, spec.ResourceId);
        CachedSprites[spriteId] = sprite;
        return sprite;
    }

    private static (
        string ResourceSuffix,
        string SpriteName,
        SettingsDockSpriteResourceId ResourceId
    ) Resolve(BppDockButtonSpriteId spriteId) =>
        spriteId switch
        {
            BppDockButtonSpriteId.ReplayExport => (
                "Resources.DockButtons.replay-export-icon.png",
                "ReplayExport",
                SettingsDockSpriteResourceId.ReplayExportIcon
            ),
            BppDockButtonSpriteId.ReplayRecording => (
                "Resources.DockButtons.replay-recording-icon.png",
                "ReplayRecording",
                SettingsDockSpriteResourceId.ReplayRecordingIcon
            ),
            BppDockButtonSpriteId.ReplayView => (
                "Resources.DockButtons.replay-view-icon.png",
                "ReplayView",
                SettingsDockSpriteResourceId.ReplayViewIcon
            ),
            BppDockButtonSpriteId.ReplayRetry => (
                "Resources.DockButtons.replay-retry-icon.png",
                "ReplayRetry",
                SettingsDockSpriteResourceId.ReplayRetryIcon
            ),
            _ => (
                "Resources.DockButtons.collection-panel-icon.png",
                "CollectionPanel",
                SettingsDockSpriteResourceId.CollectionPanelIcon
            ),
        };

    private static Sprite? LoadSprite(
        string resourceSuffix,
        string spriteName,
        SettingsDockSpriteResourceId resourceId
    )
    {
        var assembly = typeof(BppDockButtonSpriteProvider).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase)
            );
        if (resourceName == null)
        {
            ReportDegraded(SettingsLogReasonCode.ResourceMissing, resourceId);
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            ReportDegraded(SettingsLogReasonCode.ResourceStreamUnavailable, resourceId);
            return null;
        }

        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        var texture = new Texture2D(2, 2, TextureFormat.ARGB32, mipChain: false)
        {
            name = $"BPP_{spriteName}_Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        if (!texture.LoadImage(bytes.ToArray(), markNonReadable: false))
        {
            UnityEngine.Object.Destroy(texture);
            ReportDegraded(SettingsLogReasonCode.ResourceDecodeFailed, resourceId);
            return null;
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            extrude: 0u,
            SpriteMeshType.FullRect
        );
        sprite.name = $"BPP_{spriteName}_Sprite";
        return sprite;
    }

    private static void ReportDegraded(
        SettingsLogReasonCode reasonCode,
        SettingsDockSpriteResourceId resourceId
    ) =>
        BppLog.WarnEvent(
            SettingsLogEvents.DockSpriteDegraded,
            SettingsLogEvents.DockSpriteDegradedReasonCode.Bind(reasonCode),
            SettingsLogEvents.DockSpriteDegradedResourceId.Bind(resourceId)
        );
}

internal enum BppDockButtonSpriteId
{
    CollectionPanel,
    ReplayExport,
    ReplayRecording,
    ReplayView,
    ReplayRetry,
}
