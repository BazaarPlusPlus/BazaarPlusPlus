#nullable enable
using System;
using System.IO;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BepInEx;
using UnityEngine;

namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class BppUiFont
{
    private const string Component = "UiFont";
    private const string FontFileName = "LXGWWenKai-Regular.ttf";
    private const string ResourceName = "BazaarPlusPlus.Resources.Fonts.LXGWWenKai-Regular.ttf";

    private static Font? _default;

    public static Font Default => _default ??= LoadDefault();

    public static void RequestCharactersInTexture(string characters, int size, FontStyle style)
    {
        if (string.IsNullOrEmpty(characters))
            return;

        Default.RequestCharactersInTexture(characters, size, style);
    }

    private static Font LoadDefault()
    {
        var cacheRoot = Path.Combine(GetCacheRoot(), "BazaarPlusPlus", "Fonts");
        var fontPath = EmbeddedFontFile.Extract(
            Assembly.GetExecutingAssembly(),
            ResourceName,
            FontFileName,
            cacheRoot
        );
        var font = new Font(fontPath);
        BppLog.Info(Component, $"Loaded UI font '{FontFileName}' from '{fontPath}'.");
        return font;
    }

    private static string GetCacheRoot()
    {
        if (!string.IsNullOrWhiteSpace(Paths.CachePath))
            return Paths.CachePath;

        return Path.Combine(Path.GetTempPath(), "BepInEx", "cache");
    }
}
