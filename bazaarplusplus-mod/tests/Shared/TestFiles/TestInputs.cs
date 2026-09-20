#nullable enable
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace BazaarPlusPlus.TestSupport;

internal static class TestInputs
{
    private static readonly string[] SourceExtensions =
    {
        ".cs",
        ".mm",
        ".m",
        ".c",
        ".cpp",
        ".h",
        ".hpp",
        ".sh",
        ".ps1",
    };

    internal static string RepoRoot { get; } = DiscoverRepoRoot();

    internal static XDocument MsBuild(string relativePath)
    {
        var path = ResolveRepoFile(relativePath, ".csproj", ".props", ".targets");
        return XDocument.Load(path);
    }

    internal static string Markdown(string relativePath)
    {
        var path = ResolveRepoFile(relativePath, ".md");
        return File.ReadAllText(path);
    }

    internal static int LineCount(string path)
    {
        var full = Path.GetFullPath(path);
        RejectDecompiled(full);
        if (!File.Exists(full))
            throw new InvalidOperationException($"LineCount target '{path}' does not exist.");

        var count = 0;
        using var reader = new StreamReader(File.OpenRead(full));
        while (reader.ReadLine() != null)
            count++;
        return count;
    }

    internal static IReadOnlyList<EditorConfigSection> EditorConfig(string relativePath)
    {
        var path = ResolveRepoFile(relativePath);
        if (!string.Equals(Path.GetFileName(path), ".editorconfig", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"EditorConfig only accepts .editorconfig: '{relativePath}'."
            );

        var sections = new List<EditorConfigSection>();
        var header = "";
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                Flush();
                header = line[1..^1];
                properties = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }

            var split = line.IndexOf('=');
            if (split < 0)
                continue;
            properties[line[..split].Trim()] = line[(split + 1)..].Trim();
        }

        Flush();
        return sections;

        void Flush()
        {
            sections.Add(
                new EditorConfigSection(
                    header,
                    new Dictionary<string, string>(properties, StringComparer.Ordinal),
                    relativePath
                )
            );
        }
    }

    internal static IReadOnlyList<string> DataLines(string relativePath)
    {
        var path = ResolveRepoFile(relativePath, ".txt");
        return File.ReadAllLines(path);
    }

    internal static string Fixture(string relativePath)
    {
        var path = ResolveRepoFile(relativePath);
        RejectSourceExtension(path, relativePath);
        return File.ReadAllText(path);
    }

    internal static string Scratch(string path)
    {
        var full = ResolveScratch(path);
        return File.ReadAllText(full);
    }

    internal static byte[] ScratchBytes(string path)
    {
        var full = ResolveScratch(path);
        return File.ReadAllBytes(full);
    }

    internal static string RepoRelative(string path)
    {
        var full = Path.GetFullPath(path);
        if (!IsUnder(full, RepoRoot))
            throw new InvalidOperationException($"'{path}' is not under the repository root.");
        return Path.GetRelativePath(RepoRoot, full).Replace('\\', '/');
    }

    private static string ResolveRepoFile(string relativePath, params string[] allowedExtensions)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException(
                $"Repository reads take a relative path: '{relativePath}'."
            );

        var full = Path.GetFullPath(Path.Combine(RepoRoot, relativePath));
        if (!IsUnder(full, RepoRoot))
            throw new InvalidOperationException($"'{relativePath}' escapes the repository root.");
        RejectDecompiled(full);
        if (allowedExtensions.Length > 0)
            RequireExtension(full, relativePath, allowedExtensions);
        else
            RejectSourceExtension(full, relativePath);
        if (!File.Exists(full))
            throw new InvalidOperationException($"Missing repository file '{relativePath}'.");
        return full;
    }

    private static string ResolveScratch(string path)
    {
        if (!Path.IsPathRooted(path))
            throw new InvalidOperationException($"Scratch requires an absolute path: '{path}'.");

        var full = Path.GetFullPath(path);
        var allowed = IsUnder(full, Path.GetTempPath()) || IsUnder(full, AppContext.BaseDirectory);
        if (!allowed)
            throw new InvalidOperationException(
                $"Scratch path '{full}' is not under the temp directory or AppContext.BaseDirectory."
            );
        RejectDecompiled(full);
        RejectSourceExtension(full, full);
        if (!File.Exists(full))
            throw new InvalidOperationException($"Scratch file '{full}' does not exist.");
        return full;
    }

    private static void RequireExtension(string full, string display, params string[] allowed)
    {
        var extension = Path.GetExtension(full);
        foreach (var candidate in allowed)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                return;
        }

        throw new InvalidOperationException(
            $"'{display}' must end in {string.Join(" / ", allowed)}."
        );
    }

    private static void RejectSourceExtension(string full, string display)
    {
        var extension = Path.GetExtension(full);
        foreach (var banned in SourceExtensions)
        {
            if (string.Equals(extension, banned, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"TestInputs refuses source files: '{display}'."
                );
            }
        }
    }

    private static void RejectDecompiled(string full)
    {
        var relative = IsUnder(full, RepoRoot)
            ? Path.GetRelativePath(RepoRoot, full).Replace('\\', '/')
            : full.Replace('\\', '/');
        if (
            relative.StartsWith("decompiled/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("decompiled-", StringComparison.OrdinalIgnoreCase)
            || relative.Contains("/decompiled/", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                $"TestInputs refuses decompiled/ paths: '{relative}'."
            );
        }
    }

    private static bool IsUnder(string fullPath, string parent)
    {
        var prefix = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(fullPath);
        if (string.Equals(candidate, prefix, StringComparison.OrdinalIgnoreCase))
            return true;
        return candidate.StartsWith(
            prefix + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string DiscoverRepoRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from TestInputs.cs."
        );
    }
}
