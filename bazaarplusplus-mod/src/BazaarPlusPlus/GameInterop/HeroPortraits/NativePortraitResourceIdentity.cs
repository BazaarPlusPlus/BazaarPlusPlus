#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Spine.Unity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace BazaarPlusPlus.GameInterop.HeroPortraits;

internal static class NativePortraitResourceIdentity
{
    internal static string? Capture(NativeHeroPortraitSkin skin, Sprite sprite)
    {
        try
        {
            return CaptureCore(skin, sprite);
        }
        catch (Exception)
        {
            // Optional cache metadata must never prevent the normal native load path.
            return null;
        }
    }

    private static string? CaptureCore(NativeHeroPortraitSkin skin, Sprite sprite)
    {
        if (
            skin.CharacterPrefab == null
            || string.IsNullOrEmpty(skin.PortraitKey)
            || sprite == null
            || sprite.packed
        )
            return null;
        var entries = new List<string>();
        if (
            !Describe(skin.CharacterPrefab.RuntimeKey.ToString()!, typeof(GameObject), entries)
            || !Describe(skin.PortraitKey, typeof(Sprite), entries)
        )
            return null;
        entries.Sort(StringComparer.Ordinal);
        var rect = sprite.rect;
        var text =
            string.Join("\n", entries)
            + "\n"
            + string.Join(
                "|",
                Application.version,
                skin.CharacterPrefab.RuntimeKey,
                skin.PortraitKey,
                skin.IdleAnimation,
                rect.x.ToString("R", CultureInfo.InvariantCulture),
                rect.y.ToString("R", CultureInfo.InvariantCulture),
                rect.width.ToString("R", CultureInfo.InvariantCulture),
                rect.height.ToString("R", CultureInfo.InvariantCulture),
                sprite.texture.width,
                sprite.texture.height
            );
        return Hash(Encoding.UTF8.GetBytes(text));
    }

    private static bool Describe(string key, Type type, List<string> entries)
    {
        foreach (var locator in Addressables.ResourceLocators)
        {
            if (!locator.Locate(key, type, out var locations) || locations.Count == 0)
                continue;
            var pending = new Stack<IResourceLocation>(locations);
            var visited = new HashSet<IResourceLocation>();
            var bundles = 0;
            while (pending.Count != 0)
            {
                var location = pending.Pop();
                if (!visited.Add(location))
                    continue;
                if (visited.Count > 256)
                    return false;
                entries.Add(location.ProviderId + "|" + location.InternalId);
                if (location.Data is AssetBundleRequestOptions bundle)
                {
                    if (
                        (string.IsNullOrEmpty(bundle.Hash) || bundle.Hash.All(c => c == '0'))
                        && (bundle.Crc == 0 || bundle.BundleSize <= 0)
                    )
                        return false;
                    entries.Add(
                        string.Join(
                            "|",
                            bundle.BundleName,
                            bundle.Hash,
                            bundle.Crc.ToString(CultureInfo.InvariantCulture),
                            bundle.BundleSize.ToString(CultureInfo.InvariantCulture)
                        )
                    );
                    bundles++;
                }
                else if (location.Dependencies == null || location.Dependencies.Count == 0)
                    return false;
                if (location.Dependencies != null)
                    foreach (var dependency in location.Dependencies)
                        pending.Push(dependency);
            }
            return bundles != 0;
        }
        return false;
    }

    internal static string Source(SkeletonAnimation source, string animation)
    {
        var asset = source.skeletonDataAsset;
        if (asset.skeletonJSON == null)
            return "";
        var data = asset.GetSkeletonData(false);
        var resolved =
            data.FindAnimation(animation)
            ?? data.FindAnimation("idle")
            ?? data.FindAnimation(source.AnimationName ?? "");
        return string.Join(
            "|",
            Hash(asset.skeletonJSON.bytes),
            asset.scale.ToString("R", CultureInfo.InvariantCulture),
            resolved?.Name ?? animation,
            source.initialSkinName,
            source.initialFlipX,
            source.initialFlipY
        );
    }

    private static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
