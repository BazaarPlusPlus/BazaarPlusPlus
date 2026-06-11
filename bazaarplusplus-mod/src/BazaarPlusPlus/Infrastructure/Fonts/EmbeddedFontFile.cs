#nullable enable
using System;
using System.IO;
using System.Reflection;

namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class EmbeddedFontFile
{
    public static string Extract(
        Assembly assembly,
        string resourceName,
        string fileName,
        string cacheRoot
    )
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Resource name is required.", nameof(resourceName));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(cacheRoot))
            throw new ArgumentException("Cache root is required.", nameof(cacheRoot));

        Directory.CreateDirectory(cacheRoot);
        var targetPath = Path.Combine(cacheRoot, fileName);

        using var resource = assembly.GetManifestResourceStream(resourceName);
        if (resource == null)
            throw new FileNotFoundException(
                $"Embedded font resource '{resourceName}' was not found.",
                resourceName
            );

        if (File.Exists(targetPath) && new FileInfo(targetPath).Length == resource.Length)
            return targetPath;

        using var output = File.Create(targetPath);
        resource.CopyTo(output);
        return targetPath;
    }
}
