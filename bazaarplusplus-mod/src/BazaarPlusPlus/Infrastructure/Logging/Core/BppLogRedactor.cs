#nullable enable
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogRedactor
{
    private const string AbsolutePathMarker = "<absolute-path>";
    private const string InvalidUrlMarker = "<invalid-url>";

    private static readonly Regex RemoteUriPattern = new(
        @"https?://(?:[^\s\""'<>?#,;=]+[?#][^\r\n]*|[^\s\""'<>?#,;=]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex BodySensitivePattern = new(
        @"(?<![\p{L}\p{N}_])[\""']?(?:headers?|request[-_ ]?body|response[-_ ]?body)[\""']?\s*[:=][\s\S]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex LineSensitivePattern = new(
        @"(?<![\p{L}\p{N}_])[\""']?(?:authorization|proxy[-_ ]?authorization|cookie|set[-_ ]?cookie)[\""']?\s*[:=]\s*[^\r\n]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?<![\p{L}\p{N}_])[\""']?(?:access[-_ ]?token|refresh[-_ ]?token|token|api[-_ ]?key|password|secret|account[-_ ]?id|user[-_ ]?name|username|display[-_ ]?name|link[-_ ]?code)[\""']?\s*[:=]\s*(?:(?:bearer|basic)\s+)?(?:\""(?:\\.|[^\""\\\r\n])*\""|'(?:\\.|[^'\\\r\n])*'|[^\s,;\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex CredentialPattern = new(
        @"\b(?:(?:bearer|basic)\s+[^\s,;\r\n]+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly Regex UnknownAbsolutePathPattern = new(
        @"(?<![\p{L}\p{N}<>])(?:[A-Za-z]:[\\/]|\\{1,2}|/)[^\r\n]*",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex SlashProsePattern = new(
        @"(?<=[\u3400-\u4DBF\u4E00-\u9FFF\u3040-\u30FF\uAC00-\uD7AF])(?:/|[ \t]+/[ \t]+)(?=[\u3400-\u4DBF\u4E00-\u9FFF\u3040-\u30FF\uAC00-\uD7AF]+(?:(?![/\\])[\p{P}\s]|$))",
        RegexOptions.CultureInvariant
    );

    private readonly RootAlias[] _roots;

    internal BppLogRedactor(BppLogRedactionRoots roots)
    {
        var candidates = new List<RootAlias>(4);
        AddRoot(candidates, roots.DataRoot, "<bpp-data>");
        AddRoot(candidates, roots.PluginRoot, "<plugins>");
        AddRoot(candidates, roots.GameRoot, "<game>");
        AddRoot(candidates, roots.HomeRoot, "<home>");
        candidates.Sort((left, right) => right.Path.Length.CompareTo(left.Path.Length));
        _roots = candidates.ToArray();
    }

    internal string SanitizePath(string rawPath)
    {
        try
        {
            if (rawPath.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return AbsolutePathMarker;

            var normalized = NormalizePath(rawPath);
            foreach (var root in _roots)
            {
                if (!StartsWithRoot(normalized, root))
                    continue;

                var remainder =
                    normalized.Length == root.Path.Length
                        ? string.Empty
                        : normalized.Substring(root.Path.Length).TrimStart('/');
                return remainder.Length == 0 ? root.Alias : root.Alias + "/" + remainder;
            }

            if (IsAbsolutePath(normalized))
                return AbsolutePathMarker;
            return "<relative-path>";
        }
        catch
        {
            return AbsolutePathMarker;
        }
    }

    internal string SanitizeRemoteUri(string rawUri)
    {
        try
        {
            var queryIndex = rawUri.IndexOf('?');
            var fragmentIndex = rawUri.IndexOf('#');
            var sensitiveSuffixIndex =
                queryIndex < 0 ? fragmentIndex
                : fragmentIndex < 0 ? queryIndex
                : Math.Min(queryIndex, fragmentIndex);
            var queryFree =
                sensitiveSuffixIndex < 0 ? rawUri : rawUri.Substring(0, sensitiveSuffixIndex);
            queryFree = TrimUriPunctuation(queryFree);

            if (!Uri.TryCreate(queryFree, UriKind.Absolute, out var uri))
                return InvalidUrlMarker;
            if (
                !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return InvalidUrlMarker;

            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty,
            };
            return builder.Uri.GetLeftPart(UriPartial.Path);
        }
        catch
        {
            return InvalidUrlMarker;
        }
    }

    internal string SanitizeDiagnosticText(string text)
    {
        try
        {
            var sanitized = text;
            foreach (var root in _roots)
            {
                sanitized = ReplaceKnownRoot(sanitized, root, root.Path);
                var windowsPath = root.Path.Replace('/', '\\');
                if (!string.Equals(windowsPath, root.Path, StringComparison.Ordinal))
                    sanitized = ReplaceKnownRoot(sanitized, root, windowsPath);
            }

            sanitized = BodySensitivePattern.Replace(sanitized, "sensitive=<redacted>");
            sanitized = LineSensitivePattern.Replace(sanitized, "sensitive=<redacted>");
            sanitized = SensitiveAssignmentPattern.Replace(sanitized, "sensitive=<redacted>");
            sanitized = CredentialPattern.Replace(sanitized, "<redacted>");

            var remoteUris = new List<string>();
            sanitized = RemoteUriPattern.Replace(
                sanitized,
                match =>
                {
                    var marker = "BPPURIMARKER" + remoteUris.Count + "END";
                    remoteUris.Add(SanitizeRemoteUri(match.Value));
                    return marker;
                }
            );

            var slashProse = new List<string>();
            sanitized = SlashProsePattern.Replace(
                sanitized,
                match =>
                {
                    var marker = "BPPSLASHPROSE" + slashProse.Count + "END";
                    slashProse.Add(match.Value);
                    return marker;
                }
            );
            sanitized = UnknownAbsolutePathPattern.Replace(sanitized, AbsolutePathMarker);
            for (var index = 0; index < remoteUris.Count; index++)
            {
                sanitized = sanitized.Replace("BPPURIMARKER" + index + "END", remoteUris[index]);
            }
            for (var index = 0; index < slashProse.Count; index++)
            {
                sanitized = sanitized.Replace("BPPSLASHPROSE" + index + "END", slashProse[index]);
            }
            return sanitized;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static void AddRoot(List<RootAlias> roots, string? path, string alias)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var normalized = NormalizePath(path);
        if (normalized.Length == 0)
            return;

        roots.Add(new RootAlias(normalized, alias, IsWindowsPath(normalized)));
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        var isUnc = normalized.StartsWith("//", StringComparison.Ordinal);
        var isPosix = !isUnc && normalized.StartsWith("/", StringComparison.Ordinal);
        var isWindows = IsWindowsPath(normalized);
        var prefixLength =
            isUnc ? 2
            : isPosix ? 1
            : isWindows ? 3
            : 0;
        var prefix =
            isUnc ? "//"
            : isPosix ? "/"
            : isWindows ? normalized.Substring(0, 3)
            : "";
        var segments = normalized
            .Substring(Math.Min(prefixLength, normalized.Length))
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var canonical = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (canonical.Count > 0 && canonical[canonical.Count - 1] != "..")
                    canonical.RemoveAt(canonical.Count - 1);
                else if (prefixLength == 0)
                    canonical.Add(segment);
                continue;
            }
            canonical.Add(segment);
        }

        var suffix = string.Join("/", canonical);
        if (suffix.Length == 0)
            return prefix.TrimEnd('/');
        return prefix + suffix;
    }

    private static bool IsAbsolutePath(string path) =>
        path.StartsWith("/", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal)
        || IsWindowsPath(path);

    private static bool IsWindowsPath(string path) =>
        path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '/';

    private static bool StartsWithRoot(string path, RootAlias root)
    {
        var comparison = root.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(root.Path, comparison))
            return false;
        return path.Length == root.Path.Length || path[root.Path.Length] == '/';
    }

    private static string ReplaceKnownRoot(string text, RootAlias root, string path)
    {
        var comparison = root.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var searchFrom = 0;
        while (searchFrom < text.Length)
        {
            var found = text.IndexOf(path, searchFrom, comparison);
            if (found < 0)
                break;

            var hasLeadingBoundary = found == 0 || !IsPathCharacter(text[found - 1]);
            var after = found + path.Length;
            var hasTrailingBoundary =
                after == text.Length || text[after] == '/' || text[after] == '\\';
            if (!hasLeadingBoundary || !hasTrailingBoundary)
            {
                searchFrom = found + 1;
                continue;
            }

            if (ContainsTraversalSegment(text, found))
            {
                searchFrom = found + 1;
                continue;
            }

            text = text.Substring(0, found) + root.Alias + text.Substring(after);
            searchFrom = found + root.Alias.Length;
        }

        return text;
    }

    private static bool ContainsTraversalSegment(string text, int pathStart)
    {
        var end = text.IndexOfAny(new[] { '\r', '\n' }, pathStart);
        if (end < 0)
            end = text.Length;
        var candidate = text.Substring(pathStart, end - pathStart).Replace('\\', '/');
        var segments = candidate.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index] == "..")
                return true;
        }
        return false;
    }

    private static bool IsPathCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_' || value == '-' || value == '.';

    private static string TrimUriPunctuation(string value) =>
        value.TrimEnd('.', ',', ';', ':', ')', ']', '}');

    private readonly struct RootAlias
    {
        internal RootAlias(string path, string alias, bool ignoreCase)
        {
            Path = path;
            Alias = alias;
            IgnoreCase = ignoreCase;
        }

        internal string Path { get; }

        internal string Alias { get; }

        internal bool IgnoreCase { get; }
    }
}
