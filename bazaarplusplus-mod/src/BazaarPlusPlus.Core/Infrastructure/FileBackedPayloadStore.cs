#nullable enable
namespace BazaarPlusPlus.Infrastructure;

public delegate bool TryDeserialize<T>(
    byte[]? payloadBytes,
    out T? payload,
    out string? error,
    out Exception? exception
)
    where T : class;

public enum FileBackedPayloadLoadStatus
{
    Missing,
    Loaded,
    Invalid,
    Unreadable,
}

public sealed class FileBackedPayloadLoadResult<T>
    where T : class
{
    private FileBackedPayloadLoadResult(
        FileBackedPayloadLoadStatus status,
        T? payload,
        Exception? exception
    )
    {
        Status = status;
        Payload = payload;
        Exception = exception;
    }

    public FileBackedPayloadLoadStatus Status { get; }
    public T? Payload { get; }
    public Exception? Exception { get; }

    public static FileBackedPayloadLoadResult<T> Missing() =>
        new(FileBackedPayloadLoadStatus.Missing, null, null);

    public static FileBackedPayloadLoadResult<T> Loaded(T? payload) =>
        new(FileBackedPayloadLoadStatus.Loaded, payload, null);

    public static FileBackedPayloadLoadResult<T> Invalid(Exception? exception = null) =>
        new(FileBackedPayloadLoadStatus.Invalid, null, exception);

    public static FileBackedPayloadLoadResult<T> Unreadable(Exception exception) =>
        new(FileBackedPayloadLoadStatus.Unreadable, null, exception);
}

public sealed class FileBackedPayloadStore<T>
    where T : class
{
    private readonly string _rootPath;
    private readonly string _fileSuffix;
    private readonly Func<T, byte[]> _serialize;
    private readonly TryDeserialize<T> _tryDeserialize;
    private readonly int? _maximumFileBytes;

    public FileBackedPayloadStore(
        string rootPath,
        string fileSuffix,
        Func<T, byte[]> serialize,
        TryDeserialize<T> tryDeserialize,
        int? maximumFileBytes = null
    )
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(fileSuffix))
            throw new ArgumentException("File suffix is required.", nameof(fileSuffix));

        _maximumFileBytes = maximumFileBytes;
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

    public FileBackedPayloadLoadResult<T> LoadDetailed(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return FileBackedPayloadLoadResult<T>.Missing();

        var filePath = GetFilePath(battleId);
        byte[] payloadBytes;
        try
        {
            if (_maximumFileBytes is not { } maximum)
                payloadBytes = File.ReadAllBytes(filePath);
            else
            {
                using var stream = File.OpenRead(filePath);
                if (stream.Length > maximum)
                    throw new InvalidDataException(
                        $"{typeof(T).Name} exceeds the compressed size limit."
                    );
                using var output = new MemoryStream();
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > maximum)
                        throw new InvalidDataException(
                            $"{typeof(T).Name} exceeds the compressed size limit."
                        );
                    output.Write(buffer, 0, read);
                }
                payloadBytes = output.ToArray();
            }
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
            return FileBackedPayloadLoadResult<T>.Unreadable(ex);
        }

        try
        {
            if (_tryDeserialize(payloadBytes, out var payload, out var reason, out var exception))
                return FileBackedPayloadLoadResult<T>.Loaded(payload);

            return FileBackedPayloadLoadResult<T>.Invalid(
                exception
                    ?? new InvalidDataException(
                        reason == "payload_too_large"
                            ? $"{typeof(T).Name} exceeds the decompressed size limit ({reason})."
                            : $"{typeof(T).Name} decode failed: {reason ?? "payload_decode_failed"}."
                    )
            );
        }
        catch (Exception ex)
        {
            return FileBackedPayloadLoadResult<T>.Invalid(ex);
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
}
