#nullable enable
using System.Text.RegularExpressions;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

// The documentation tree has been swept by hand three times (dbd4e6c5, 3f1f682c, 05467e44) and
// regrown to roughly five times its post-sweep size within a month each time, because a sweep
// leaves nothing behind that can fail. These tests are what a sweep leaves behind.
//
// Each one guards a failure mode observed in the last regrowth, not a style preference:
// - always-loaded files growing past what they cost an agent per turn;
// - file references surviving the file they point at (dbd4e6c5 left three of them in src/).
public class DocsHygieneTests
{
    // Budgets are bytes, not lines. These files are dense one-line entries, so a line count says
    // nothing about their cost: MEMORY.md sat at 93 lines against a "200 line" budget while being
    // twice its own stated byte ceiling. Each number is roughly 20% above the file's size when the
    // budget was set, so ordinary edits pass and sustained growth does not.
    private static readonly (string Path, int MaxBytes)[] AlwaysLoadedBudgets =
    {
        ("AGENTS.md", 9 * 1024),
        ("CONTEXT.md", 13 * 1024),
        ("docs/MEMORY.md", 17 * 1024),
        ("docs/ARCHITECTURE.md", 14 * 1024),
        ("docs/README.md", 6 * 1024),
    };

    [Fact]
    public void Always_loaded_docs_stay_within_their_byte_budgets()
    {
        var repoRoot = TestInputs.RepoRoot;
        var overBudget = new List<string>();

        foreach (var (relative, maxBytes) in AlwaysLoadedBudgets)
        {
            var path = Path.Combine(repoRoot, relative);
            Assert.True(File.Exists(path), $"Budgeted document '{relative}' is missing.");

            var actual = new FileInfo(path).Length;
            if (actual > maxBytes)
            {
                overBudget.Add(
                    $"{relative}: {actual} B exceeds its {maxBytes} B budget by {actual - maxBytes} B."
                );
            }
        }

        Assert.True(
            overBudget.Count == 0,
            "Always-loaded documentation is over budget. Merge entries into existing ones rather "
                + "than appending, or move detail behind a pointer. Do not compress MEMORY.md's "
                + "Gotchas section to fit — that section is why the file exists; take the space "
                + "from Durable knowledge or Patterns instead.\n  "
                + string.Join("\n  ", overBudget)
        );
    }

    [Fact]
    public void Documentation_file_references_resolve()
    {
        var repoRoot = TestInputs.RepoRoot;
        var broken = new List<string>();

        // Only path-shaped references are checked. A bare `RunLogSchema.cs` in prose names a type's
        // file without promising a location, and treating it as a path produces noise that trains
        // people to ignore this test.
        var reference = new Regex(
            @"`(?<path>[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+\.(?:cs|csproj|json|sh|props|targets|md))(?::(?<start>\d+)(?:-(?<end>\d+))?)?`",
            RegexOptions.Compiled
        );

        foreach (var doc in MarkdownFiles(repoRoot))
        {
            var relativeDoc = Path.GetRelativePath(repoRoot, doc).Replace('\\', '/');
            var lineNumber = 0;
            foreach (var line in SplitLines(TestInputs.Markdown(relativeDoc)))
            {
                lineNumber++;
                foreach (Match match in reference.Matches(line))
                {
                    var target = match.Groups["path"].Value;
                    if (IsLocalDecompileReference(repoRoot, target))
                        continue;
                    var resolved = ResolveReference(repoRoot, target);
                    if (resolved == null)
                    {
                        broken.Add($"{relativeDoc}:{lineNumber} -> `{target}` does not exist.");
                        continue;
                    }

                    if (!match.Groups["start"].Success)
                        continue;

                    var total = TestInputs.LineCount(resolved);
                    var start = int.Parse(match.Groups["start"].Value);
                    var end = match.Groups["end"].Success
                        ? int.Parse(match.Groups["end"].Value)
                        : start;
                    if (start > total || end > total)
                    {
                        broken.Add(
                            $"{relativeDoc}:{lineNumber} -> `{target}` cites line "
                                + $"{match.Groups[0].Value.Split(':').Last().TrimEnd('`')} but the file has {total} lines."
                        );
                    }
                }
            }
        }

        Assert.True(
            broken.Count == 0,
            "Documentation points at files or lines that no longer exist. Cite the file without a "
                + "line number when the anchor is a whole type or method — a bare path stays true "
                + "across edits that a line range does not.\n  "
                + string.Join("\n  ", broken)
        );
    }

    [Fact]
    public void Documentation_links_resolve()
    {
        var repoRoot = TestInputs.RepoRoot;
        var link = new Regex(@"\[[^\]]*\]\((?<target>[^)]+)\)", RegexOptions.Compiled);
        var broken = new List<string>();

        foreach (var doc in MarkdownFiles(repoRoot))
        {
            var relativeDoc = Path.GetRelativePath(repoRoot, doc).Replace('\\', '/');
            var docDir = Path.GetDirectoryName(doc)!;
            var lineNumber = 0;
            foreach (var line in SplitLines(TestInputs.Markdown(relativeDoc)))
            {
                lineNumber++;
                foreach (Match match in link.Matches(line))
                {
                    var target = match.Groups["target"].Value.Split('#')[0];
                    if (
                        target.Length == 0
                        || target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    )
                        continue;

                    if (IsLocalDecompileReference(repoRoot, target))
                        continue;
                    var resolved = Path.GetFullPath(Path.Combine(docDir, target));
                    if (!File.Exists(resolved) && !Directory.Exists(resolved))
                        broken.Add($"{relativeDoc}:{lineNumber} -> {target}");
                }
            }
        }

        Assert.True(
            broken.Count == 0,
            "Markdown links point at missing files. A sweep that deletes a document must also "
                + "retarget everything that linked to it.\n  "
                + string.Join("\n  ", broken)
        );
    }

    private static bool IsLocalDecompileReference(string repoRoot, string target)
    {
        var normalized = target.Replace('\\', '/').TrimStart('.', '/');
        if (
            !normalized.StartsWith("decompiled/", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("decompiled-v", StringComparison.OrdinalIgnoreCase)
        )
            return false;

        return !Directory.Exists(Path.Combine(repoRoot, "decompiled"))
            && !Directory.Exists(Path.Combine(repoRoot, "decompiled-vptr"));
    }

    private static string? ResolveReference(string repoRoot, string target)
    {
        // References are written relative to the repo root, and inside architecture prose also
        // relative to the main source tree.
        foreach (var prefix in new[] { "", "src/BazaarPlusPlus/" })
        {
            var candidate = Path.Combine(
                repoRoot,
                prefix.Replace('/', Path.DirectorySeparatorChar),
                target
            );
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> MarkdownFiles(string repoRoot)
    {
        foreach (var relative in new[] { "AGENTS.md", "CONTEXT.md", "README.md" })
        {
            var path = Path.Combine(repoRoot, relative);
            if (File.Exists(path))
                yield return path;
        }

        var docs = Path.Combine(repoRoot, "docs");
        if (!Directory.Exists(docs))
            yield break;

        foreach (var path in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
            yield return path;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }
}
