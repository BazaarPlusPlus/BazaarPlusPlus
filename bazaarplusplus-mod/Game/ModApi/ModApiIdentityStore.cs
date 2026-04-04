#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiIdentityStore
{
    private readonly string _identityPath;
    private readonly object _sync = new();
    private string? _cachedInstallId;

    public ModApiIdentityStore(string identityPath)
    {
        if (string.IsNullOrWhiteSpace(identityPath))
            throw new ArgumentException("Identity path is required.", nameof(identityPath));

        _identityPath = identityPath;
    }

    public string GetOrCreateInstallId()
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(_cachedInstallId))
                return _cachedInstallId;

            if (File.Exists(_identityPath))
            {
                var existing = File.ReadAllText(_identityPath).Trim();
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    _cachedInstallId = existing;
                    return existing;
                }
            }

            var directory = Path.GetDirectoryName(_identityPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            _cachedInstallId = Guid.NewGuid().ToString("N");
            File.WriteAllText(_identityPath, $"{_cachedInstallId}{Environment.NewLine}");
            return _cachedInstallId;
        }
    }
}
