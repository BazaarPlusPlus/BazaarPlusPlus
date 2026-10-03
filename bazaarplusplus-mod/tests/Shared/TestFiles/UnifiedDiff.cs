#nullable enable
using System.Text;

namespace BazaarPlusPlus.TestSupport;

/// <summary>A minimal line diff (LCS) with three lines of context, for golden mismatches.</summary>
internal static class UnifiedDiff
{
    internal static string Render(string expected, string actual, string label)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        for (var j = b.Length - 1; j >= 0; j--)
            lengths[i, j] =
                a[i] == b[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var lines = new List<(char Kind, string Text)>();
        int x = 0,
            y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                lines.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (x < a.Length && (y == b.Length || lengths[x + 1, y] >= lengths[x, y + 1]))
                lines.Add(('-', a[x++]));
            else
                lines.Add(('+', b[y++]));
        }

        const int context = 3;
        var output = new StringBuilder($"--- {label}\n+++ actual\n");
        var printedThrough = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Kind == ' ')
                continue;
            var start = Math.Max(printedThrough + 1, index - context);
            if (start > printedThrough + 1)
                output.Append("@@\n");
            var end = index;
            while (
                end + 1 < lines.Count
                && (
                    lines[end + 1].Kind != ' '
                    || lines.Skip(end + 1).Take(context * 2).Any(line => line.Kind != ' ')
                )
            )
                end++;
            end = Math.Min(lines.Count - 1, end + context);
            for (var line = start; line <= end; line++)
                output.Append(lines[line].Kind).Append(lines[line].Text).Append('\n');
            printedThrough = end;
            index = end;
        }
        return output.ToString();
    }
}
