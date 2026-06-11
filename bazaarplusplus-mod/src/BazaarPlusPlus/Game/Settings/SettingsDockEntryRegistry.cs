#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class SettingsDockEntryRegistry
{
    private readonly List<ISettingsDockEntry> _entries = new();

    public void Register(ISettingsDockEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));
        _entries.Add(entry);
    }

    public IReadOnlyList<BppSettingsDockDefinition> MaterializeAll(IBppConfig config)
    {
        var result = new List<BppSettingsDockDefinition>(_entries.Count);
        foreach (var entry in _entries)
            result.Add(entry.Build(config));
        return result;
    }

    public IReadOnlyList<(int Order, BppSettingsDockDefinition Definition)> MaterializeWithOrder(
        IBppConfig config
    )
    {
        var result = new List<(int, BppSettingsDockDefinition)>(_entries.Count);
        foreach (var entry in _entries)
            result.Add((entry.Order, entry.Build(config)));
        return result;
    }
}
