#nullable enable
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotUploadRecord
{
    public string SnapshotId { get; init; } = string.Empty;

    public BazaarDbSnapshotUploadRequest Payload { get; init; } =
        new BazaarDbSnapshotUploadRequest();
}
