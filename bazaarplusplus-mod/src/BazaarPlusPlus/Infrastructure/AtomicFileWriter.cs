#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Infrastructure;

internal static class AtomicFileWriter
{
    public static void Write(string filePath, byte[] bytes)
    {
        Write(filePath, tempPath => File.WriteAllBytes(tempPath, bytes));
    }

    public static void Write(string filePath, Action<string> writeToTempPath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            writeToTempPath(tempPath);
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
