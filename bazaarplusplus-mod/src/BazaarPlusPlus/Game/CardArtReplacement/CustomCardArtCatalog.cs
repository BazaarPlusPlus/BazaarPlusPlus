#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class CustomCardArtCatalog
{
    private readonly Dictionary<Guid, string> _pathsByTemplateId = new();

    public CustomCardArtCatalog(string? directoryPath)
    {
        DirectoryPath = directoryPath;
        Refresh();
    }

    public string? DirectoryPath { get; }

    public int Count => _pathsByTemplateId.Count;

    public void Refresh()
    {
        _pathsByTemplateId.Clear();
        if (string.IsNullOrWhiteSpace(DirectoryPath) || !Directory.Exists(DirectoryPath))
            return;

        foreach (var path in Directory.EnumerateFiles(DirectoryPath))
        {
            if (!CustomCardArtImageFormats.TryGetTemplateId(path, out var templateId))
                continue;

            _pathsByTemplateId[templateId] = path;
        }
    }

    public bool TryGetArtPath(Guid templateId, out string path)
    {
        if (templateId != Guid.Empty && _pathsByTemplateId.TryGetValue(templateId, out path!))
            return true;

        path = string.Empty;
        return false;
    }
}
