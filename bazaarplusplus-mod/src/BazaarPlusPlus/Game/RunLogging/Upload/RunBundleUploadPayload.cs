#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal sealed class RunBundleUploadSnapshot
{
    public RunBundleUploadRequest Metadata { get; set; } = new();

    public byte[] ArtifactBytes { get; set; } = [];

    public string RunId { get; set; } = string.Empty;

    public long LastSeq { get; set; }

    public string? UploadedStatus { get; set; }

    public IReadOnlyList<string> BattleIds { get; set; } = new List<string>();
}
