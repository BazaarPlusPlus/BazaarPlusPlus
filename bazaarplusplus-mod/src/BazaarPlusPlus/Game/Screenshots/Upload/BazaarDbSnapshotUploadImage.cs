#nullable enable
namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadImage
{
    public byte[] Bytes { get; set; } = [];

    public string ContentType { get; set; } = "image/png";

    public string SourcePath { get; set; } = string.Empty;
}
