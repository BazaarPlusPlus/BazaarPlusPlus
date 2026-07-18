#nullable enable
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentDecisionLogEntry
{
    public string Ts { get; set; } = "";
    public ulong TickId { get; set; }
    public string DecisionId { get; set; } = "";
    public string? RunId { get; set; }
    public string State { get; set; } = "";
    public BazaarAgentAction Action { get; set; } = new();
    public bool Executed { get; set; }
    public string? Error { get; set; }
    public string? Reason { get; set; }
}

public sealed class BazaarAgentDecisionLog
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    private readonly string _rootDir;

    public BazaarAgentDecisionLog(string rootDir)
    {
        _rootDir = rootDir;
    }

    public void Append(BazaarAgentDecisionLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Ts))
            entry.Ts = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        var target = ResolveTarget(entry.RunId);
        var dir = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(dir);

        var json = JsonConvert.SerializeObject(entry, Settings);
        File.AppendAllText(target, json + "\n", Utf8NoBom);
    }

    private string ResolveTarget(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return Path.Combine(_rootDir, "decisions.jsonl");

        var sanitized = SanitizeRunId(runId);
        return Path.Combine(_rootDir, "runs", sanitized, "decisions.jsonl");
    }

    private static string SanitizeRunId(string runId)
    {
        // Build a set of all characters that must be replaced
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(runId.Length);
        foreach (var ch in runId)
        {
            if (ch == '.' || ch == '/' || ch == '\\' || Array.IndexOf(invalidChars, ch) >= 0)
                sb.Append('_');
            else
                sb.Append(ch);
        }
        var result = sb.ToString();
        return result.Length == 0 ? "_" : result;
    }
}
