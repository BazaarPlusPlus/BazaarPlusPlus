#nullable enable
// The Launch Record exists only inside the Android App, and its reader needs System.Text.Json,
// which netstandard2.1 lacks: the desktop builds (Core's netstandard2.1 target and the BepInEx
// patcher) compile none of this file.
#if NET
using System.Text.Json;

namespace BazaarPlusPlus.ManagedPayload;

/// <summary>How a launch runs the mod, as its Launch Record says (root GLOSSARY.md).</summary>
public enum LaunchRecordMode
{
    /// <summary>No record: the mod is not running in the Android App, and runs normally.</summary>
    Absent,

    /// <summary>A record for this process and this release, in mode <c>normal</c>.</summary>
    Normal,

    /// <summary>
    /// Mode <c>compatibility</c>, or a record that cannot be trusted: unreadable, invalid,
    /// another process's, or another release's. Uploads stay off (root ADR 0012).
    /// </summary>
    Compatibility,
}

/// <summary>
/// The Launch Record the Android App writes into LemonLoader's <c>UserData/</c> before every
/// BPP launch (root ADR 0012, "启动记录"): which App, release, mode, game and interop this launch
/// uses. The App's writer owns the format and a shared golden,
/// <c>bazaarplusplus-android/contracts/launch-record.v1.json</c>, that this reader is tested
/// against. Parsing is strict: a missing, mistyped or unknown field, an unknown
/// <c>schemaVersion</c> or <c>mode</c>, is no record at all.
/// </summary>
public sealed class LaunchRecord
{
    /// <summary>The record's file name in LemonLoader's <c>UserData/</c> directory.</summary>
    public const string FileName = "BazaarPlusPlus.launch.json";

    /// <summary>The one schema this reader knows; a field change bumps it.</summary>
    public const int SchemaVersion = 1;

    // A record is a few hundred bytes; anything this large was not written by the App.
    private const int MaxBytes = 64 * 1024;

    private LaunchRecord(
        int processId,
        LaunchRecordApp app,
        string release,
        string mode,
        LaunchRecordGame game,
        LaunchRecordInterop interop
    )
    {
        ProcessId = processId;
        App = app;
        Release = release;
        Mode = mode;
        Game = game;
        Interop = interop;
    }

    /// <summary>The process that wrote the record, which the game then runs in.</summary>
    public int ProcessId { get; }

    public LaunchRecordApp App { get; }

    /// <summary>The product version this launch stages or has installed.</summary>
    public string Release { get; }

    /// <summary><c>normal</c> or <c>compatibility</c>.</summary>
    public string Mode { get; }

    public LaunchRecordGame Game { get; }

    public LaunchRecordInterop Interop { get; }

    public bool IsCompatibilityMode => Mode == "compatibility";

    /// <summary>The record's path under LemonLoader's <c>UserData/</c> directory.</summary>
    public static string PathIn(string userDataDirectory) =>
        Path.Combine(userDataDirectory, FileName);

    /// <summary>Parses one record; throws <see cref="LaunchRecordFormatException"/> otherwise.</summary>
    public static LaunchRecord Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException exception)
        {
            throw new LaunchRecordFormatException("invalid_json", exception.Message);
        }

        using (document)
        {
            var root = RequireObject(document.RootElement, "record");
            // The schema first, so a newer record says so rather than "unknown field".
            if (
                !root.TryGetProperty("schemaVersion", out var schema)
                || schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out var schemaVersion)
            )
                throw new LaunchRecordFormatException("invalid", "schemaVersion is missing.");
            if (schemaVersion != SchemaVersion)
                throw new LaunchRecordFormatException(
                    "schema_unknown",
                    $"schemaVersion {schemaVersion} is not {SchemaVersion}."
                );
            Fields(
                root,
                "record",
                "schemaVersion",
                "processId",
                "app",
                "release",
                "mode",
                "game",
                "interop"
            );

            var mode = Text(root, "mode");
            if (mode != "normal" && mode != "compatibility")
                throw new LaunchRecordFormatException("mode_unknown", $"mode '{mode}'.");
            var release = Text(root, "release");
            if (!ManagedPayloadVersion.TryParse(release, out _) || release.Split('.').Length != 3)
                throw new LaunchRecordFormatException("invalid", $"release '{release}'.");
            var processId = Integer(root, "processId");

            var app = Child(root, "app");
            Fields(
                app,
                "app",
                "packageName",
                "versionName",
                "versionCode",
                "nonManagedPayloadDigest"
            );
            var appDigest = app.GetProperty("nonManagedPayloadDigest");
            string? digest =
                appDigest.ValueKind == JsonValueKind.Null
                    ? null
                    : Sha256(app, "nonManagedPayloadDigest");

            var game = Child(root, "game");
            Fields(
                game,
                "game",
                "packageName",
                "versionName",
                "versionCode",
                "il2cppSha256",
                "metadataSha256"
            );

            var interop = Child(root, "interop");
            Fields(interop, "interop", "sha256", "snapshotSha256", "source");
            var source = Text(interop, "source");
            if (source != "release" && source != "pre-adaptation")
                throw new LaunchRecordFormatException("invalid", $"interop.source '{source}'.");

            return new LaunchRecord(
                processId,
                new LaunchRecordApp(
                    Text(app, "packageName"),
                    Text(app, "versionName"),
                    Integer(app, "versionCode"),
                    digest
                ),
                release,
                mode,
                new LaunchRecordGame(
                    Text(game, "packageName"),
                    Text(game, "versionName"),
                    Integer(game, "versionCode"),
                    Sha256(game, "il2cppSha256"),
                    Sha256(game, "metadataSha256")
                ),
                new LaunchRecordInterop(
                    Sha256(interop, "sha256"),
                    Sha256(interop, "snapshotSha256"),
                    source
                )
            );
        }
    }

    /// <summary>Reads the file; null when it does not exist. Throws on an unreadable one.</summary>
    internal static string? ReadText(string path)
    {
        if (!File.Exists(path))
            return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes)
            throw new LaunchRecordFormatException("invalid", $"{stream.Length} bytes.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonElement RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new LaunchRecordFormatException("invalid", $"{name} is not an object.");
        return element;
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        RequireObject(Property(parent, name), name);

    // Every field named, each once, and no other: a field the App adds bumps schemaVersion.
    private static void Fields(JsonElement element, string name, params string[] expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(expected, property.Name) < 0)
                throw new LaunchRecordFormatException(
                    "invalid",
                    $"{name}.{property.Name} is not a field."
                );
            if (!seen.Add(property.Name))
                throw new LaunchRecordFormatException(
                    "invalid",
                    $"{name}.{property.Name} appears twice."
                );
        }
        if (seen.Count != expected.Length)
            throw new LaunchRecordFormatException(
                "invalid",
                $"{name} lacks {string.Join(",", expected.Where(field => !seen.Contains(field)))}."
            );
    }

    private static JsonElement Property(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
            ? value
            : throw new LaunchRecordFormatException("invalid", $"{name} is missing.");

    private static string Text(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Trim().Length == 0)
            throw new LaunchRecordFormatException("invalid", $"{name} is not a string.");
        return value.GetString()!;
    }

    private static int Integer(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        if (
            value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var number)
            || number <= 0
        )
            throw new LaunchRecordFormatException("invalid", $"{name} is not a positive integer.");
        return number;
    }

    private static string Sha256(JsonElement parent, string name)
    {
        var value = Text(parent, name);
        if (
            value.Length != 64
            || value.Any(c => !(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        )
            throw new LaunchRecordFormatException("invalid", $"{name} is not a sha256.");
        return value;
    }
}

/// <summary>
/// The Android App that wrote the record. <c>NonManagedPayloadDigest</c> is the digest of the
/// runtime the App carries (loader, native libraries, Updater); null for a development build of
/// the App.
/// </summary>
public sealed record LaunchRecordApp(
    string PackageName,
    string VersionName,
    int VersionCode,
    string? NonManagedPayloadDigest
);

/// <summary>The installed official game this launch loads.</summary>
public sealed record LaunchRecordGame(
    string PackageName,
    string VersionName,
    int VersionCode,
    string Il2CppSha256,
    string MetadataSha256
);

/// <summary>The Interop Package this launch loads; <c>source</c> is <c>release</c> or <c>pre-adaptation</c>.</summary>
public sealed record LaunchRecordInterop(string Sha256, string SnapshotSha256, string Source);

/// <summary>A record that does not parse; <see cref="ReasonCode"/> names why for the log.</summary>
public sealed class LaunchRecordFormatException : FormatException
{
    public LaunchRecordFormatException(string reasonCode, string message)
        : base(message) => ReasonCode = reasonCode;

    public string ReasonCode { get; }
}

/// <summary>
/// One read of the Launch Record, classified (root ADR 0012): no file is
/// <see cref="LaunchRecordMode.Absent"/>; a record that is unreadable or invalid, has an unknown
/// schema or mode, was written by another process, or names another release than the running
/// Mods is <see cref="LaunchRecordMode.Compatibility"/>, as is mode <c>compatibility</c>.
/// <see cref="Record"/> keeps a parsed record written for this process, whatever its mode, for
/// the Updater and self-update, which compare against the runtime the App started.
/// </summary>
public sealed class LaunchRecordReading
{
    private LaunchRecordReading(LaunchRecordMode mode, string reason, LaunchRecord? record)
    {
        Mode = mode;
        Reason = reason;
        Record = record;
    }

    /// <summary>No record: the mod runs outside the Android App.</summary>
    public static LaunchRecordReading Absent { get; } =
        new(LaunchRecordMode.Absent, "absent", null);

    public LaunchRecordMode Mode { get; }

    /// <summary>
    /// <c>absent</c>, <c>normal</c>, <c>mode_compatibility</c>, <c>unreadable</c>,
    /// <c>invalid</c>, <c>invalid_json</c>, <c>schema_unknown</c>, <c>mode_unknown</c>,
    /// <c>process_mismatch</c> or <c>release_mismatch</c>.
    /// </summary>
    public string Reason { get; }

    /// <summary>The parsed record when this process wrote it; null otherwise.</summary>
    public LaunchRecord? Record { get; }

    /// <summary>
    /// Reads the record at <paramref name="path"/>. <paramref name="runningRelease"/> is the
    /// version of the Mods running; null skips that comparison (the Updater runs before any Mods
    /// load, and the record's release is the version it may swap in).
    /// </summary>
    public static LaunchRecordReading Read(string path, int processId, string? runningRelease)
    {
        string? text;
        try
        {
            text = LaunchRecord.ReadText(path);
        }
        catch (LaunchRecordFormatException exception)
        {
            return Untrusted(exception.ReasonCode);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Untrusted("unreadable");
        }
        return text == null ? Absent : Classify(text, processId, runningRelease);
    }

    /// <summary>Classifies a record's text, as <see cref="Read"/> does a file's.</summary>
    public static LaunchRecordReading Classify(string text, int processId, string? runningRelease)
    {
        LaunchRecord record;
        try
        {
            record = LaunchRecord.Parse(text);
        }
        catch (LaunchRecordFormatException exception)
        {
            return Untrusted(exception.ReasonCode);
        }
        if (record.ProcessId != processId)
            return Untrusted("process_mismatch");
        if (runningRelease != null && !SameRelease(record.Release, runningRelease))
            return new(LaunchRecordMode.Compatibility, "release_mismatch", record);
        return record.IsCompatibilityMode
            ? new(LaunchRecordMode.Compatibility, "mode_compatibility", record)
            : new(LaunchRecordMode.Normal, "normal", record);
    }

    /// <summary>A record that cannot be trusted, for <paramref name="reason"/>: Compatibility Mode.</summary>
    public static LaunchRecordReading Untrusted(string reason) =>
        new(LaunchRecordMode.Compatibility, reason, null);

    private static bool SameRelease(string release, string running) =>
        ManagedPayloadVersion.TryParse(release, out var recorded)
        && ManagedPayloadVersion.TryParse(running, out var current)
        && recorded == current;
}
#endif
