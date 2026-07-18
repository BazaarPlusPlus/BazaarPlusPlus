#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

/// <summary>
/// A settings dock row backed by an ordered value ladder, rendered as a native toggle or
/// dropdown choice.
/// </summary>
/// <remarks>
/// The <c>write</c> callback owns persistence and any coupled persistence (for example,
/// forcing another setting on). The optional <c>onChanged</c> callback reacts after the
/// write completes (for example, publishing an event, refreshing UI, or arming a pump) and
/// is invoked unconditionally with the new value after every write.
/// </remarks>
internal sealed class CyclingSettingsDockEntry<T> : ISettingsDockEntry
{
    private readonly string _key;
    private readonly Func<string, string> _resolveLabel;
    private readonly IReadOnlyList<T> _ladder;
    private readonly Func<IBppConfig, T> _read;
    private readonly Action<IBppConfig, T> _write;
    private readonly Func<T, bool> _highlightWhen;
    private readonly Func<T, string, string> _resolveStatus;
    private readonly Action<T>? _onChanged;
    private readonly bool _renderAsToggle;

    internal CyclingSettingsDockEntry(
        int order,
        string key,
        Func<string, string> resolveLabel,
        IReadOnlyList<T> ladder,
        Func<IBppConfig, T> read,
        Action<IBppConfig, T> write,
        Func<T, bool> highlightWhen,
        Func<T, string, string> resolveStatus,
        Action<T>? onChanged = null,
        bool renderAsToggle = false
    )
    {
        Order = order;
        _key = !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new ArgumentException("Key is required.", nameof(key));
        _resolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        _ladder = ladder ?? throw new ArgumentNullException(nameof(ladder));
        if (_ladder.Count == 0)
            throw new ArgumentException("At least one ladder value is required.", nameof(ladder));

        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _highlightWhen = highlightWhen ?? throw new ArgumentNullException(nameof(highlightWhen));
        _resolveStatus = resolveStatus ?? throw new ArgumentNullException(nameof(resolveStatus));
        _onChanged = onChanged;
        _renderAsToggle = renderAsToggle;
    }

    public int Order { get; }

    public BppSettingsDockDefinition Build(IBppConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (_renderAsToggle && typeof(T) == typeof(bool))
        {
            return BppSettingsDockDefinition.Toggle(
                _key,
                _resolveLabel,
                () => (bool)(object)_read(config)!,
                enabled => Write(config, (T)(object)enabled)
            );
        }

        return BppSettingsDockDefinition.Choice(
            _key,
            _resolveLabel,
            () => _highlightWhen(_read(config)),
            languageCode => ResolveChoiceState(config, languageCode),
            standardIndex => SelectStandardChoice(config, standardIndex)
        );
    }

    internal static CyclingSettingsDockEntry<bool> Toggle(
        int order,
        string key,
        Func<string, string> resolveLabel,
        Func<IBppConfig, bool> read,
        Action<IBppConfig, bool> write,
        Action<bool>? onChanged = null
    ) =>
        new(
            order,
            key,
            resolveLabel,
            new[] { false, true },
            read,
            write,
            value => value,
            (value, _) => value ? "ON" : "OFF",
            onChanged: onChanged,
            renderAsToggle: true
        );

    private BppSettingsChoiceState ResolveChoiceState(IBppConfig config, string languageCode)
    {
        var current = _read(config);
        var comparer = EqualityComparer<T>.Default;
        var selectedStandardIndex = -1;
        for (var index = 0; index < _ladder.Count; index++)
        {
            if (comparer.Equals(_ladder[index], current))
            {
                selectedStandardIndex = index;
                break;
            }
        }

        var hasSyntheticCurrent = selectedStandardIndex < 0;
        var options = new List<string>(_ladder.Count + (hasSyntheticCurrent ? 1 : 0));
        if (hasSyntheticCurrent)
            options.Add(_resolveStatus(current, languageCode));

        foreach (var value in _ladder)
            options.Add(_resolveStatus(value, languageCode));

        return new BppSettingsChoiceState(
            options,
            hasSyntheticCurrent ? 0 : selectedStandardIndex,
            hasSyntheticCurrent
        );
    }

    private void SelectStandardChoice(IBppConfig config, int standardIndex)
    {
        if (standardIndex < 0 || standardIndex >= _ladder.Count)
            return;

        Write(config, _ladder[standardIndex]);
    }

    private void Write(IBppConfig config, T value)
    {
        _write(config, value);
        _onChanged?.Invoke(value);
    }
}
