using System.Globalization;
using System.Text;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// Android settings store: an INI file at <c>MelonLoader/UserData/BazaarPlusPlus.cfg</c> in the
/// BepInEx cfg syntax, so both hosts' settings files have the same shape. A bind reads the
/// persisted value (unparsable or missing falls back to the default) and rewrites the file when
/// that changed it; every write persists. Keys nothing binds survive a rewrite, as in BepInEx.
/// </summary>
internal sealed class IniSettingsStore : IBppSettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<(string Section, string Key, string Value)> _persisted;
    private readonly List<IEntry> _bound = [];

    private IniSettingsStore(
        string path,
        List<(string Section, string Key, string Value)> persisted
    )
    {
        _path = path;
        _persisted = persisted;
    }

    public static IniSettingsStore Open(string path)
    {
        var persisted = new List<(string Section, string Key, string Value)>();
        if (File.Exists(path))
        {
            var section = string.Empty;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                var split = line.IndexOf('=', StringComparison.Ordinal);
                if (split <= 0)
                    continue;
                var key = line[..split].Trim();
                persisted.RemoveAll(entry => entry.Section == section && entry.Key == key);
                persisted.Add((section, key, line[(split + 1)..].Trim()));
            }
        }

        return new IniSettingsStore(path, persisted);
    }

    public ISetting<T> Bind<T>(
        string section,
        string key,
        T defaultValue,
        string description,
        SettingRange<T>? acceptableRange = null
    )
        where T : IComparable
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var type = typeof(T);
        if (
            !type.IsEnum
            && type != typeof(string)
            && type != typeof(bool)
            && type != typeof(int)
            && type != typeof(float)
            && type != typeof(double)
        )
            throw new NotSupportedException($"Setting type {type.Name} has no INI form.");
        lock (_gate)
        {
            if (_bound.Any(entry => entry.Section == section && entry.Key == key))
                throw new InvalidOperationException($"Setting '{section}.{key}' is already bound.");

            var stored = _persisted.FindIndex(entry =>
                entry.Section == section && entry.Key == key
            );
            var storedText = stored >= 0 ? _persisted[stored].Value : null;
            var value = defaultValue;
            if (storedText != null && TryParse(storedText, out T parsed))
                value = parsed;

            var entry = new Entry<T>(this, section, key, description, defaultValue, acceptableRange)
            {
                Current = Clamp(value, acceptableRange),
            };
            _bound.Add(entry);
            if (stored >= 0)
                _persisted.RemoveAt(stored);
            if (entry.Serialized != storedText)
                Save();
            return entry;
        }
    }

    private void Save()
    {
        var text = new StringBuilder();
        var sections = _bound
            .Select(entry => entry.Section)
            .Concat(_persisted.Select(entry => entry.Section))
            .Distinct(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            text.Append('[').Append(section).Append("]\n\n");
            foreach (var entry in _bound.Where(entry => entry.Section == section))
                entry.Write(text);
            foreach (var (_, key, value) in _persisted.Where(entry => entry.Section == section))
                text.Append(key).Append(" = ").Append(value).Append("\n\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var staging = _path + ".tmp";
        File.WriteAllText(staging, text.ToString(), new UTF8Encoding(false));
        File.Move(staging, _path, overwrite: true);
    }

    private static T Clamp<T>(T value, SettingRange<T>? range)
        where T : IComparable
    {
        if (range == null)
            return value;
        if (value.CompareTo(range.Min) < 0)
            return range.Min;
        return value.CompareTo(range.Max) > 0 ? range.Max : value;
    }

    private static bool TryParse<T>(string text, out T value)
    {
        object? parsed = null;
        var type = typeof(T);
        var invariant = CultureInfo.InvariantCulture;
        if (type == typeof(string))
            parsed = text;
        else if (type == typeof(bool) && bool.TryParse(text, out var flag))
            parsed = flag;
        else if (
            type == typeof(int)
            && int.TryParse(text, NumberStyles.Integer, invariant, out var i)
        )
            parsed = i;
        else if (
            type == typeof(float)
            && float.TryParse(text, NumberStyles.Float, invariant, out var f)
        )
            parsed = f;
        else if (
            type == typeof(double)
            && double.TryParse(text, NumberStyles.Float, invariant, out var d)
        )
            parsed = d;
        else if (type.IsEnum && Enum.TryParse(type, text, ignoreCase: true, out var member))
            parsed = Enum.IsDefined(type, member) ? member : null;

        value = parsed is T typed ? typed : default!;
        return parsed is T;
    }

    private static string Serialize<T>(T value) =>
        value switch
        {
            bool flag => flag ? "true" : "false",
            Enum member => member.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? string.Empty,
        };

    private interface IEntry
    {
        string Section { get; }
        string Key { get; }
        void Write(StringBuilder text);
    }

    private sealed class Entry<T> : ISetting<T>, IEntry
        where T : IComparable
    {
        private readonly IniSettingsStore _store;
        private readonly string _description;
        private readonly T _default;
        private readonly SettingRange<T>? _range;

        public Entry(
            IniSettingsStore store,
            string section,
            string key,
            string description,
            T defaultValue,
            SettingRange<T>? range
        )
        {
            _store = store;
            Section = section;
            Key = key;
            _description = description;
            _default = defaultValue;
            _range = range;
        }

        public string Section { get; }

        public string Key { get; }

        public T Current { get; set; } = default!;

        public string Serialized => Serialize(Current);

        public T Value
        {
            get
            {
                lock (_store._gate)
                    return Current;
            }
            set
            {
                lock (_store._gate)
                {
                    Current = Clamp(value, _range);
                    _store.Save();
                }
            }
        }

        public void Write(StringBuilder text)
        {
            foreach (var line in _description.Split('\n'))
                text.Append("## ").Append(line.TrimEnd('\r')).Append('\n');
            text.Append("# Setting type: ").Append(typeof(T).Name).Append('\n');
            text.Append("# Default value: ").Append(Serialize(_default)).Append('\n');
            if (_range != null)
                text.Append("# Acceptable value range: From ")
                    .Append(Serialize(_range.Min))
                    .Append(" to ")
                    .Append(Serialize(_range.Max))
                    .Append('\n');
            if (typeof(T).IsEnum)
                text.Append("# Acceptable values: ")
                    .Append(string.Join(", ", Enum.GetNames(typeof(T))))
                    .Append('\n');
            text.Append(Key).Append(" = ").Append(Serialized).Append("\n\n");
        }
    }
}
