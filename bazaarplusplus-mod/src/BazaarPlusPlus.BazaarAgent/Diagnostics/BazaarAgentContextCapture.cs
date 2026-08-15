#nullable enable
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentContextCapture
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

    private readonly string _snapshotsDirectory;
    private readonly string _metricsPath;

    public BazaarAgentContextCapture(string rootDirectory, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException(
                "Capture root directory is required.",
                nameof(rootDirectory)
            );

        sessionId = string.IsNullOrWhiteSpace(sessionId) ? CreateSessionId() : sessionId;
        SessionDirectory = Path.Combine(rootDirectory, "captures", SanitizeSegment(sessionId));
        _snapshotsDirectory = Path.Combine(SessionDirectory, "snapshots");
        _metricsPath = Path.Combine(SessionDirectory, "metrics.jsonl");
    }

    public string SessionDirectory { get; }

    public void Capture(BazaarAgentContextSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        Directory.CreateDirectory(_snapshotsDirectory);

        var context = snapshot.Context;
        var payloadJson = JsonConvert.SerializeObject(context, Settings);
        var snapshotFileName =
            snapshot.TickId.ToString("D8", CultureInfo.InvariantCulture)
            + "-"
            + context.StateName.ToString().ToLowerInvariant()
            + ".json";
        File.WriteAllText(
            Path.Combine(_snapshotsDirectory, snapshotFileName),
            payloadJson,
            Utf8NoBom
        );

        var topLevelCards = context
            .BoardItems.Concat(context.ChestItems)
            .Concat(context.PlayerSkills)
            .Concat(context.SellableItems)
            .Concat(context.SelectionOptions)
            .ToArray();
        var actionEmbeddedCards = context
            .AvailableActions.Where(static action => action.Card is not null)
            .Select(static action => action.Card!)
            .ToArray();
        var allFullCardCopies = topLevelCards.Concat(actionEmbeddedCards).ToArray();
        var uniqueCardInstanceIds = allFullCardCopies
            .Select(static card => card.InstanceId)
            .Where(static instanceId => !string.IsNullOrWhiteSpace(instanceId))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var metrics = new BazaarAgentContextCaptureMetrics
        {
            CapturedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            SchemaVersion = context.SchemaVersion,
            TickId = snapshot.TickId,
            StateName = context.StateName.ToString(),
            RunId = context.RunId,
            SnapshotFile = Path.Combine("snapshots", snapshotFileName).Replace('\\', '/'),
            PayloadBytes = Utf8NoBom.GetByteCount(payloadJson),
            BoardItemCount = context.BoardItems.Count,
            ChestItemCount = context.ChestItems.Count,
            PlayerSkillCount = context.PlayerSkills.Count,
            SellableItemCount = context.SellableItems.Count,
            SelectionOptionCount = context.SelectionOptions.Count,
            AvailableActionCount = context.AvailableActions.Count,
            ActionCounts = context
                .AvailableActions.GroupBy(
                    static action => action.ActionKind.ToString(),
                    StringComparer.Ordinal
                )
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count()),
            TopLevelFullCardCopies = topLevelCards.Length,
            ActionEmbeddedFullCardCopies = actionEmbeddedCards.Length,
            TotalFullCardCopies = allFullCardCopies.Length,
            UniqueCardInstanceIds = uniqueCardInstanceIds,
            RedundantFullCardCopies = Math.Max(0, allFullCardCopies.Length - uniqueCardInstanceIds),
            ActionCardReferenceCount = context.AvailableActions.Count(static action =>
                !string.IsNullOrWhiteSpace(action.CardInstanceId)
            ),
            PlacementActionCount = context.AvailableActions.Count(static action =>
                action.ActionKind
                    is BazaarAgentActionKind.MoveItem
                        or BazaarAgentActionKind.SelectItem
            ),
            TargetSocketValueCount = context.AvailableActions.Sum(static action =>
                action.TargetSockets?.Count ?? 0
            ),
            TagValueCount = allFullCardCopies.Sum(static card => card.Tags.Count),
            HiddenTagValueCount = allFullCardCopies.Sum(static card => card.HiddenTags.Count),
            DescriptionCharacterCount = allFullCardCopies.Sum(static card =>
                card.Description?.Length ?? 0
            ),
            CooldownValueCount = allFullCardCopies.Count(static card =>
                card.CooldownSeconds is > 0
            ),
            AmmoCapacityCount = allFullCardCopies.Count(static card => card.AmmoMax is > 0),
            SectionBytes = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["boardItems"] = SerializedBytes(context.BoardItems),
                ["chestItems"] = SerializedBytes(context.ChestItems),
                ["playerSkills"] = SerializedBytes(context.PlayerSkills),
                ["sellableItems"] = SerializedBytes(context.SellableItems),
                ["selectionOptions"] = SerializedBytes(context.SelectionOptions),
                ["availableActions"] = SerializedBytes(context.AvailableActions),
            },
        };

        var metricsJson = JsonConvert.SerializeObject(metrics, Settings);
        File.AppendAllText(_metricsPath, metricsJson + "\n", Utf8NoBom);
    }

    private static int SerializedBytes(object value) =>
        Utf8NoBom.GetByteCount(JsonConvert.SerializeObject(value, Settings));

    private static string CreateSessionId() =>
        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss.fff'Z'", CultureInfo.InvariantCulture)
        + "-"
        + Guid.NewGuid().ToString("N")[..8];

    private static string SanitizeSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(
                character == '.'
                || character == '/'
                || character == '\\'
                || Array.IndexOf(invalidChars, character) >= 0
                    ? '_'
                    : character
            );
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }
}

public sealed class BazaarAgentContextCaptureMetrics
{
    public string CapturedAtUtc { get; init; } = "";
    public string SchemaVersion { get; init; } = "";
    public ulong TickId { get; init; }
    public string StateName { get; init; } = "";
    public string? RunId { get; init; }
    public string SnapshotFile { get; init; } = "";
    public int PayloadBytes { get; init; }
    public int BoardItemCount { get; init; }
    public int ChestItemCount { get; init; }
    public int PlayerSkillCount { get; init; }
    public int SellableItemCount { get; init; }
    public int SelectionOptionCount { get; init; }
    public int AvailableActionCount { get; init; }
    public IReadOnlyDictionary<string, int> ActionCounts { get; init; } =
        new Dictionary<string, int>();
    public int TopLevelFullCardCopies { get; init; }
    public int ActionEmbeddedFullCardCopies { get; init; }
    public int TotalFullCardCopies { get; init; }
    public int UniqueCardInstanceIds { get; init; }
    public int RedundantFullCardCopies { get; init; }
    public int ActionCardReferenceCount { get; init; }
    public int PlacementActionCount { get; init; }
    public int TargetSocketValueCount { get; init; }
    public int TagValueCount { get; init; }
    public int HiddenTagValueCount { get; init; }
    public int DescriptionCharacterCount { get; init; }
    public int CooldownValueCount { get; init; }
    public int AmmoCapacityCount { get; init; }
    public IReadOnlyDictionary<string, int> SectionBytes { get; init; } =
        new Dictionary<string, int>();
}
