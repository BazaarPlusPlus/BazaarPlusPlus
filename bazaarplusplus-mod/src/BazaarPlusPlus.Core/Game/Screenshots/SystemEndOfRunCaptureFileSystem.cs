#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

public sealed class SystemEndOfRunCaptureFileSystem : IEndOfRunCaptureFileSystem
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public bool IsUsablePng(string? filePath)
    {
        if (
            string.IsNullOrWhiteSpace(filePath)
            || !string.Equals(
                Path.GetExtension(filePath),
                ".png",
                StringComparison.OrdinalIgnoreCase
            )
            || !File.Exists(filePath)
        )
            return false;

        using var stream = File.OpenRead(filePath);
        if (stream.Length <= PngSignature.Length)
            return false;
        for (var index = 0; index < PngSignature.Length; index++)
        {
            if (stream.ReadByte() != PngSignature[index])
                return false;
        }
        return true;
    }

    public void DeleteIfExists(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            File.Delete(filePath);
    }
}
