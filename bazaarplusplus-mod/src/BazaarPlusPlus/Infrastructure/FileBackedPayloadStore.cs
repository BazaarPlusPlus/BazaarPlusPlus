#nullable enable
using System.Security.Cryptography;

namespace BazaarPlusPlus.Infrastructure;

internal delegate bool TryDeserialize<T>(byte[]? payloadBytes, out T? payload, out string? error)
    where T : class;

internal enum FileBackedPayloadLoadStatus
{
    Missing,
    Loaded,
    Invalid,
    Unreadable,
}

internal sealed class FileBackedPayloadLoadResult<T>
    where T : class
{
    private FileBackedPayloadLoadResult(
        FileBackedPayloadLoadStatus status,
        T? payload,
        string? fingerprint,
        Exception? exception
    )
    {
        Status = status;
        Payload = payload;
        Fingerprint = fingerprint;
        Exception = exception;
    }

    internal FileBackedPayloadLoadStatus Status { get; }
    internal T? Payload { get; }
    internal string? Fingerprint { get; }
    internal Exception? Exception { get; }

    internal static FileBackedPayloadLoadResult<T> Missing() =>
        new(FileBackedPayloadLoadStatus.Missing, null, null, null);

    internal static FileBackedPayloadLoadResult<T> Loaded(T? payload, string fingerprint) =>
        new(FileBackedPayloadLoadStatus.Loaded, payload, fingerprint, null);

    internal static FileBackedPayloadLoadResult<T> Invalid(
        string fingerprint,
        Exception? exception = null
    ) => new(FileBackedPayloadLoadStatus.Invalid, null, fingerprint, exception);

    internal static FileBackedPayloadLoadResult<T> Unreadable(
        string fingerprint,
        Exception exception
    ) => new(FileBackedPayloadLoadStatus.Unreadable, null, fingerprint, exception);
}

internal sealed class FileBackedPayloadStore<T>
    where T : class
{
    private readonly string _rootPath;
    private readonly string _fileSuffix;
    private readonly Func<T, byte[]> _serialize;
    private readonly TryDeserialize<T> _tryDeserialize;

    public FileBackedPayloadStore(
        string rootPath,
        string fileSuffix,
        Func<T, byte[]> serialize,
        TryDeserialize<T> tryDeserialize
    )
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(fileSuffix))
            throw new ArgumentException("File suffix is required.", nameof(fileSuffix));

        _rootPath = rootPath;
        _fileSuffix = fileSuffix;
        _serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
        _tryDeserialize = tryDeserialize ?? throw new ArgumentNullException(nameof(tryDeserialize));

        Directory.CreateDirectory(_rootPath);
    }

    public void Save(string battleId, T payload)
    {
        AtomicFileWriter.Write(GetFilePath(battleId), _serialize(payload));
    }

    public T? Load(string battleId)
    {
        var result = LoadDetailed(battleId);
        return result.Status == FileBackedPayloadLoadStatus.Loaded ? result.Payload : null;
    }

    internal FileBackedPayloadLoadResult<T> LoadDetailed(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return FileBackedPayloadLoadResult<T>.Missing();

        var filePath = GetFilePath(battleId);
        byte[] payloadBytes;
        try
        {
            payloadBytes = File.ReadAllBytes(filePath);
        }
        catch (FileNotFoundException)
        {
            return FileBackedPayloadLoadResult<T>.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return FileBackedPayloadLoadResult<T>.Missing();
        }
        catch (Exception ex)
        {
            return FileBackedPayloadLoadResult<T>.Unreadable(
                FingerprintUnreadableFile(filePath),
                ex
            );
        }

        var fingerprint = Fingerprint(payloadBytes);
        try
        {
            if (_tryDeserialize(payloadBytes, out var payload, out _))
                return FileBackedPayloadLoadResult<T>.Loaded(payload, fingerprint);

            return FileBackedPayloadLoadResult<T>.Invalid(fingerprint);
        }
        catch (Exception ex)
        {
            return FileBackedPayloadLoadResult<T>.Invalid(fingerprint, ex);
        }
    }

    public bool Exists(string battleId)
    {
        return !string.IsNullOrWhiteSpace(battleId) && File.Exists(GetFilePath(battleId));
    }

    public void Delete(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return;

        var filePath = GetFilePath(battleId);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public IEnumerable<string> ListIds()
    {
        Directory.CreateDirectory(_rootPath);

        foreach (var filePath in Directory.EnumerateFiles(_rootPath, $"*{_fileSuffix}"))
        {
            var fileName = Path.GetFileName(filePath);
            if (
                fileName.EndsWith(_fileSuffix, StringComparison.OrdinalIgnoreCase)
                && fileName.Length > _fileSuffix.Length
            )
            {
                yield return fileName[..^_fileSuffix.Length];
            }
        }
    }

    private string GetFilePath(string battleId)
    {
        return Path.Combine(_rootPath, $"{battleId}{_fileSuffix}");
    }

    private static string Fingerprint(byte[] payloadBytes)
    {
        using var sha256 = SHA256.Create();
        return BitConverter
            .ToString(sha256.ComputeHash(payloadBytes))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static string FingerprintUnreadableFile(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return $"metadata:{info.Length:x}:{info.LastWriteTimeUtc.Ticks:x}";
        }
        catch
        {
            return "metadata:unavailable";
        }
    }
}
