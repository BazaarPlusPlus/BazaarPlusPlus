#nullable enable
using System.Text;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceLineCatalog
{
    private static readonly object SyncRoot = new();
    private static CatalogSnapshot _snapshot = CatalogSnapshot.Empty;

    private static readonly IReadOnlyDictionary<string, string> CharacterAliases = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["Dooley"] = "Dooley",
        ["Jules"] = "Jules",
        ["Karnok"] = "Karnok",
        ["Mak"] = "Mak",
        ["Pyg"] = "Pygmalien",
        ["Pygmalien"] = "Pygmalien",
        ["Stelle"] = "Stelle",
        ["Vanessa"] = "Vanessa",
    };

    private static readonly VoiceLine[] SampleLines =
    {
        new("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f),
        new(
            "002_VanessaUpgrade1",
            "Ooh, the keener the blade, the quicker the fight.",
            "嚯，刀刃越利，胜负越快。",
            3.42f
        ),
        new(
            "003_VanessaPvPIntro2",
            "You should know, I'm a bit of a legend on Polago.",
            "你该知道，我在波拉戈也算个传奇。",
            3.38f
        ),
        new(
            "004_VanessaRunVictory2",
            "Read the tides and listen to the wind, they will guide your strikes.",
            "看潮汐，听海风，它们会指引你的出手。",
            5.59f
        ),
        new(
            "005_VanessaLastLife2",
            "Ships may sink, but I'm a good swimmer.",
            "船会沉，但我水性不错。",
            3.27f
        ),
        new(
            "006_VanessaPvEVictory2",
            "Was that even a battle? Maybe a skirmish?",
            "那也算战斗吗？顶多是一场小冲突。",
            3.4f
        ),
        new(
            "007_VanessaNoBuyGold1",
            "Heh, must be a hole in my coin purse.",
            "呵，看来我的钱袋漏了个洞。",
            3.08f
        ),
        new(
            "009_VanessaIdle3",
            "You remind me of my first voyage around the globe. Unbearably slow.",
            "你让我想起第一次环球航行，慢得让人难以忍受。",
            5.37f
        ),
        new("013_VanessaLevelUp4", "Rally, men! Blades to the wind!", "船员们，迎风举刃！", 2.11f),
        new("014_VanessaNoBuySpace4", "Maybe if we tow the barge.", "也许拖艘驳船来就行。", 2.16f),
        new(
            "024_VanessaMultiClick3",
            "My father was the first captain to resist, and he paid the price for it.",
            "我的父亲是第一个奋起反抗的船长，也为此付出了代价。",
            5.63f
        ),
    };

    private static readonly Entry[] SampleEntries = CreateEntries(SampleLines);

    private static readonly IReadOnlyDictionary<string, string> HookTokens = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["OnIdle"] = "Idle",
        ["OnMultiClick"] = "MultiClick",
        ["OnLastLife"] = "LastLife",
        ["OnNoBuyGold"] = "NoBuyGold",
        ["OnNoBuySpace"] = "NoBuySpace",
        ["OnPvEVictoryDefeat"] = "PvEVictory",
        ["OnPvPIntro"] = "PvPIntro",
        ["OnPvPVictoryDefeat"] = "PvPDefeat",
        ["OnRunVictoryDefeat"] = "RunVictory",
        ["OnLevelUp"] = "LevelUp",
        ["OnUpgrade"] = "Upgrade",
    };

    private static readonly CharacterProbe[] CharacterProbes = CreateCharacterProbes();

    internal static void ReplaceCatalog(VoiceLine[] lines, string catalogName)
    {
        lock (SyncRoot)
        {
            var snapshot = CatalogSnapshot.Create(lines, catalogName);
            Volatile.Write(ref _snapshot, snapshot);
        }
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            Volatile.Write(ref _snapshot, CatalogSnapshot.Empty);
        }
    }

    public static VoiceLine Resolve(string? eventReferenceText, string sourceLabel, string hookName)
    {
        return ResolveDetailed(eventReferenceText, sourceLabel, hookName).Line;
    }

    public static VoiceLineResolution ResolveDetailed(
        string? eventReferenceText,
        string sourceLabel,
        string hookName
    )
    {
        if (!string.IsNullOrWhiteSpace(eventReferenceText))
        {
            var exact = ResolveExactDetailed(eventReferenceText!);
            if (!string.IsNullOrEmpty(exact.Line.Stem))
                return exact;
        }

        if (HookTokens.TryGetValue(hookName, out var tokenFromHook))
        {
            var characterName = ResolveCharacterName(eventReferenceText);
            if (!string.IsNullOrEmpty(characterName))
            {
                var characterResolution = ResolveCharacterHookFallback(
                    characterName!,
                    tokenFromHook,
                    hookName
                );
                if (characterResolution.CandidateCount > 0)
                    return characterResolution;
            }
        }

        return new VoiceLineResolution(default, "unresolved", hookName, "none");
    }

    private static VoiceLineResolution ResolveExactDetailed(string eventReferenceText)
    {
        var normalizedTokens = ExtractLookupTokens(eventReferenceText);
        var catalog = SnapshotCatalog();

        foreach (var token in normalizedTokens)
        {
            if (token.Length < 8)
                continue;

            if (catalog.ExactByNormalizedStem.TryGetValue(token, out var exactLine))
                return new VoiceLineResolution(
                    exactLine,
                    "event-stem",
                    exactLine.Stem,
                    catalog.Name
                );
        }

        foreach (var entry in catalog.Entries)
        {
            if (MatchesExactStem(eventReferenceText, normalizedTokens, entry))
                return new VoiceLineResolution(
                    entry.Line,
                    "event-stem",
                    entry.Line.Stem,
                    catalog.Name
                );
        }

        foreach (var entry in SampleEntries)
        {
            if (MatchesExactStem(eventReferenceText, normalizedTokens, entry))
                return new VoiceLineResolution(entry.Line, "event-stem", entry.Line.Stem, "sample");
        }

        return default;
    }

    private static bool MatchesExactStem(
        string lookupText,
        string[] normalizedLookupTokens,
        Entry entry
    )
    {
        var stem = entry.Line.Stem;
        if (string.IsNullOrEmpty(stem))
            return false;

        if (lookupText.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (entry.NormalizedStem.Length < 8)
            return false;

        foreach (var token in normalizedLookupTokens)
        {
            if (token.Length < 8)
                continue;

            if (
                token.Equals(entry.NormalizedStem, StringComparison.OrdinalIgnoreCase)
                || token.IndexOf(entry.NormalizedStem, StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ExtractLookupTokens(string lookupText)
    {
        if (string.IsNullOrEmpty(lookupText))
            return Array.Empty<string>();

        List<string>? tokens = null;
        var tokenStart = -1;
        for (var i = 0; i <= lookupText.Length; i++)
        {
            var isSeparator = i == lookupText.Length || IsLookupSeparator(lookupText[i]);
            if (!isSeparator)
            {
                if (tokenStart < 0)
                    tokenStart = i;
                continue;
            }

            if (tokenStart < 0)
                continue;

            var token = NormalizeVoiceStem(lookupText, tokenStart, i - tokenStart);
            if (token.Length > 0)
            {
                tokens ??= new List<string>();
                tokens.Add(token);
            }

            tokenStart = -1;
        }

        return tokens == null ? Array.Empty<string>() : tokens.ToArray();
    }

    private static string NormalizeVoiceStem(string value)
    {
        return NormalizeVoiceStem(value, 0, value.Length);
    }

    private static string NormalizeVoiceStem(string value, int offset, int length)
    {
        var start = offset;
        var end = Math.Min(value.Length, offset + length);
        while (start < end && char.IsDigit(value[start]))
            start++;
        if (start < end && value[start] == '_')
            start++;

        var builder = new StringBuilder(Math.Max(0, end - start));
        for (var i = start; i < end; i++)
        {
            var c = value[i];
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Replace("pygmalien", "pyg", StringComparison.Ordinal);
    }

    private static VoiceLineResolution ResolveCharacterHookFallback(
        string characterName,
        string token,
        string hookName
    )
    {
        var catalog = SnapshotCatalog();
        var matchedToken = CharacterHookKey(characterName, token);
        if (!catalog.CharacterHookFallback.TryGetValue(matchedToken, out var bucket))
            return default;

        if (bucket.Count == 1)
        {
            return new VoiceLineResolution(
                bucket.SingleLine,
                "character-hook-unique",
                matchedToken,
                catalog.Name,
                bucket.Count
            );
        }

        if (bucket.Count > 1)
        {
            return new VoiceLineResolution(
                default,
                "character-hook-ambiguous",
                matchedToken,
                catalog.Name,
                bucket.Count
            );
        }

        return default;
    }

    private static bool IsCharacterLine(string stem, string characterName)
    {
        return stem.IndexOf(characterName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ResolveCharacterName(string? eventReferenceText)
    {
        if (string.IsNullOrWhiteSpace(eventReferenceText))
            return null;

        foreach (var probe in CharacterProbes)
        {
            if (
                eventReferenceText.IndexOf(probe.PathSegment, StringComparison.OrdinalIgnoreCase)
                    >= 0
                || eventReferenceText.IndexOf(probe.VoPrefix, StringComparison.OrdinalIgnoreCase)
                    >= 0
                || eventReferenceText.IndexOf(probe.Infix, StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return probe.Canonical;
            }
        }

        return null;
    }

    private static CatalogSnapshot SnapshotCatalog()
    {
        return Volatile.Read(ref _snapshot);
    }

    private static Entry[] CreateEntries(VoiceLine[]? lines)
    {
        var source = lines ?? Array.Empty<VoiceLine>();
        var entries = new Entry[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var line = source[i];
            entries[i] = new Entry(line, NormalizeVoiceStem(line.Stem ?? string.Empty));
        }

        return entries;
    }

    private static CharacterProbe[] CreateCharacterProbes()
    {
        var probes = new CharacterProbe[CharacterAliases.Count];
        var index = 0;
        foreach (var pair in CharacterAliases)
        {
            var alias = pair.Key;
            probes[index++] = new CharacterProbe(
                "/" + alias + "/",
                "VO_" + alias + "_",
                "_" + alias + "_",
                pair.Value
            );
        }

        return probes;
    }

    private static bool IsLookupSeparator(char value)
    {
        return value == ' '
            || value == '/'
            || value == '\\'
            || value == '\t'
            || value == '\r'
            || value == '\n';
    }

    private static string CharacterHookKey(string characterName, string token)
    {
        return characterName + ":" + token;
    }

    private readonly struct Entry
    {
        public Entry(VoiceLine line, string normalizedStem)
        {
            Line = line;
            NormalizedStem = normalizedStem;
        }

        public VoiceLine Line { get; }

        public string NormalizedStem { get; }
    }

    private readonly struct CharacterHookBucket
    {
        public CharacterHookBucket(int count, VoiceLine singleLine)
        {
            Count = count;
            SingleLine = singleLine;
        }

        public int Count { get; }

        public VoiceLine SingleLine { get; }

        public CharacterHookBucket Add(VoiceLine line)
        {
            return Count == 0
                ? new CharacterHookBucket(1, line)
                : new CharacterHookBucket(Count + 1, default);
        }
    }

    private readonly struct CharacterProbe
    {
        public CharacterProbe(string pathSegment, string voPrefix, string infix, string canonical)
        {
            PathSegment = pathSegment;
            VoPrefix = voPrefix;
            Infix = infix;
            Canonical = canonical;
        }

        public string PathSegment { get; }

        public string VoPrefix { get; }

        public string Infix { get; }

        public string Canonical { get; }
    }

    private sealed class CatalogSnapshot
    {
        public static readonly CatalogSnapshot Empty = new(
            Array.Empty<Entry>(),
            new Dictionary<string, VoiceLine>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CharacterHookBucket>(StringComparer.OrdinalIgnoreCase),
            "empty"
        );

        private CatalogSnapshot(
            Entry[] entries,
            Dictionary<string, VoiceLine> exactByNormalizedStem,
            Dictionary<string, CharacterHookBucket> characterHookFallback,
            string name
        )
        {
            Entries = entries;
            ExactByNormalizedStem = exactByNormalizedStem;
            CharacterHookFallback = characterHookFallback;
            Name = name;
        }

        public Entry[] Entries { get; }

        public Dictionary<string, VoiceLine> ExactByNormalizedStem { get; }

        public Dictionary<string, CharacterHookBucket> CharacterHookFallback { get; }

        public string Name { get; }

        public static CatalogSnapshot Create(VoiceLine[]? lines, string catalogName)
        {
            var entries = CreateEntries(lines);
            var exactByNormalizedStem = new Dictionary<string, VoiceLine>(
                StringComparer.OrdinalIgnoreCase
            );
            for (var i = 0; i < entries.Length; i++)
            {
                var normalizedStem = entries[i].NormalizedStem;
                if (normalizedStem.Length < 8)
                    continue;
                if (!exactByNormalizedStem.ContainsKey(normalizedStem))
                    exactByNormalizedStem.Add(normalizedStem, entries[i].Line);
            }

            return new CatalogSnapshot(
                entries,
                exactByNormalizedStem,
                BuildCharacterHookFallbackIndex(entries),
                string.IsNullOrWhiteSpace(catalogName) ? "unknown" : catalogName
            );
        }

        private static Dictionary<string, CharacterHookBucket> BuildCharacterHookFallbackIndex(
            Entry[] entries
        )
        {
            var buckets = new Dictionary<string, CharacterHookBucket>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var entry in entries)
            {
                var stem = entry.Line.Stem;
                if (string.IsNullOrEmpty(stem))
                    continue;

                HashSet<string>? seenKeys = null;
                foreach (var characterAlias in CharacterAliases)
                {
                    var character = characterAlias.Value;
                    if (!IsCharacterLine(stem, character))
                        continue;

                    foreach (var hookToken in HookTokens)
                    {
                        var token = hookToken.Value;
                        if (stem.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        var key = CharacterHookKey(character, token);
                        seenKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (!seenKeys.Add(key))
                            continue;

                        buckets.TryGetValue(key, out var bucket);
                        buckets[key] = bucket.Add(entry.Line);
                    }
                }
            }

            return buckets;
        }
    }
}
