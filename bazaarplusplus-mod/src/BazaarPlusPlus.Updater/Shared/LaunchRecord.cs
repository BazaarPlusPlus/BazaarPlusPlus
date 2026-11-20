#nullable enable
// The record reader needs System.Text.Json, which netstandard2.1 does not supply.
#if NET
using System.Text.Json;

namespace BazaarPlusPlus.ManagedPayload;

public enum LaunchRecordMode
{
    Absent,

    Normal,

    Untrusted,
}

// The Android App owns the format. Both sides share the golden
// bazaarplusplus-android/contracts/launch-record.v1.json.
public sealed class LaunchRecord
{
    public const string FileName = "BazaarPlusPlus.launch.json";

    // Any field change requires a schema version bump.
    public const int SchemaVersion = 1;

    // A record is a few hundred bytes; anything this large was not written by the App.
    private const int MaxBytes = 64 * 1024;

    private LaunchRecord(
        int processId,
        LaunchRecordApp app,
        string release,
        LaunchRecordGame game,
        LaunchRecordInterop interop
    )
    {
        ProcessId = processId;
        App = app;
        Release = release;
        Game = game;
        Interop = interop;
    }

    public int ProcessId { get; }

    public LaunchRecordApp App { get; }

    public string Release { get; }

    public LaunchRecordGame Game { get; }

    public LaunchRecordInterop Interop { get; }

    public static string PathIn(string userDataDirectory) =>
        Path.Combine(userDataDirectory, FileName);

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
                "game",
                "interop"
            );

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
            Fields(interop, "interop", "sha256", "snapshotSha256");

            return new LaunchRecord(
                processId,
                new LaunchRecordApp(
                    Text(app, "packageName"),
                    Text(app, "versionName"),
                    Integer(app, "versionCode"),
                    digest
                ),
                release,
                new LaunchRecordGame(
                    Text(game, "packageName"),
                    Text(game, "versionName"),
                    Integer(game, "versionCode"),
                    Sha256(game, "il2cppSha256"),
                    Sha256(game, "metadataSha256")
                ),
                new LaunchRecordInterop(
                    Sha256(interop, "sha256"),
                    Sha256(interop, "snapshotSha256")
                )
            );
        }
    }

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

// A development App has no runtime payload digest, so it writes null.
public sealed record LaunchRecordApp(
    string PackageName,
    string VersionName,
    int VersionCode,
    string? NonManagedPayloadDigest
);

public sealed record LaunchRecordGame(
    string PackageName,
    string VersionName,
    int VersionCode,
    string Il2CppSha256,
    string MetadataSha256
);

public sealed record LaunchRecordInterop(string Sha256, string SnapshotSha256);

public sealed class LaunchRecordFormatException : FormatException
{
    public LaunchRecordFormatException(string reasonCode, string message)
        : base(message) => ReasonCode = reasonCode;

    public string ReasonCode { get; }
}

// Keep a parsed record from this process even when its release differs from the running Mods.
// The Updater and self-update still need it to identify the runtime.
public sealed class LaunchRecordReading
{
    private LaunchRecordReading(LaunchRecordMode mode, string reason, LaunchRecord? record)
    {
        Mode = mode;
        Reason = reason;
        Record = record;
    }

    public static LaunchRecordReading Absent { get; } =
        new(LaunchRecordMode.Absent, "absent", null);

    public LaunchRecordMode Mode { get; }

    public string Reason { get; }

    public LaunchRecord? Record { get; }

    // The Updater runs before the Mods load. A null runningRelease lets it read the record
    // for the version it may swap in.
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
            return new(LaunchRecordMode.Untrusted, "release_mismatch", record);
        return new(LaunchRecordMode.Normal, "normal", record);
    }

    public static LaunchRecordReading Untrusted(string reason) =>
        new(LaunchRecordMode.Untrusted, reason, null);

    private static bool SameRelease(string release, string running) =>
        ManagedPayloadVersion.TryParse(release, out var recorded)
        && ManagedPayloadVersion.TryParse(running, out var current)
        && recorded == current;
}
#endif
