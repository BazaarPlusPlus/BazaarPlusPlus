#nullable enable
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.Identity;

internal static class IdentityJsonFileStore
{
    private static readonly JsonSerializerSettings SerializerSettings =
        new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        };

    internal static string ObservationFileName => "observation.v1.json";

    internal static string ObservationPath(string identityDirectoryPath) =>
        Path.Combine(identityDirectoryPath, ObservationFileName);

    internal static bool TryRead<T>(string filePath, out T? payload)
        where T : class
    {
        payload = null;
        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            payload = JsonConvert.DeserializeObject<T>(json, SerializerSettings);
            return payload != null;
        }
        catch
        {
            payload = null;
            return false;
        }
    }

    internal static void Write<T>(string filePath, T payload)
        where T : class
    {
        var json = JsonConvert.SerializeObject(payload, SerializerSettings);
        WriteStringAtomically(filePath, json);
    }

    private static void WriteStringAtomically(string filePath, string contents)
    {
        var directoryPath =
            Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Identity path must have a parent directory.");
        Directory.CreateDirectory(directoryPath);

        var tempPath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp"
        );

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
