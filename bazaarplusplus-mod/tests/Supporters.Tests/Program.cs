using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.TestSupport;

TestAttributionText();
TestEmptyInputReturnsNoSample();
TestInvalidEntriesAreIgnored();
TestTierWeightsSelectDefaultBronzeSilverAndGoldBuckets();
TestTierBucketSelectionUsesUniformIndexRanges();
TestSampleManyReturnsDistinctSupporters();
TestSampleManyRotatesThroughEverySupporterBeforeRepeating();
TestSampleManyDispersesLongNamesAcrossAttributionWindows();
TestSampleManyLongNameDispersalKeepsFullRotation();
TestFixedSupporterListSourceIgnoresRemoteEntries();
TestDefaultSupporterListSourceUsesRemoteEntriesBeforeFallback();
TestCatalogDocumentFiltersInvalidEntries();
TestCatalogDocumentRejectsEmptyPayload();
TestEmbeddedCatalogSeedHasAtLeastFiveEntries();
TestSupportedByPrefixAndSuffix();
TestSponsorActionText();
TestSponsorLinks();

Console.WriteLine("Supporters checks passed.");

static void TestCatalogDocumentFiltersInvalidEntries()
{
    var result = SupporterCatalogDocument.Parse(
        """[{"name":" Alice ","tier":4},{"name":" ","tier":3},{"name":"Bob","tier":0}]"""
    );

    AssertTrue(result.Succeeded, "A document with one renderable supporter should parse.");
    AssertEqual(1, result.Value!.Count, "Invalid supporter entries should be removed.");
    AssertEqual("Alice", result.Value[0].Name, "Parsed supporter names should be trimmed.");
}

static void TestCatalogDocumentRejectsEmptyPayload()
{
    var result = SupporterCatalogDocument.Parse("[]");

    AssertFalse(result.Succeeded, "An empty supporter payload must not replace a snapshot.");
    AssertEqual("empty_payload", result.Error, "Empty payloads should have a closed reason.");
}

static void TestEmbeddedCatalogSeedHasAtLeastFiveEntries()
{
    var document = TestInputs.Scratch(
        Path.Combine(AppContext.BaseDirectory, "supporter-list.json")
    );
    var result = SupporterCatalogDocument.Parse(document);

    AssertTrue(result.Succeeded, "The embedded supporter seed must parse.");
    AssertTrue(result.Value!.Count >= 5, "The embedded supporter seed must contain five entries.");
}

static void TestAttributionText()
{
    AssertEqual(
        "Supported by Alice",
        BPPSupporterAttributionText.FormatSupportedBy("Alice", "en"),
        "English attribution should preserve the existing Supported by wording."
    );
    AssertEqual(
        "由 Alice 支持",
        BPPSupporterAttributionText.FormatSupportedBy("Alice", "zh-CN"),
        "Chinese attribution should preserve the existing localized wording."
    );
    AssertEqual(
        "由 Alice 支持",
        BPPSupporterAttributionText.FormatSupportedBy(" Alice ", "zh-Hant"),
        "Attribution should trim supporter names before formatting."
    );
    AssertEqual(
        string.Empty,
        BPPSupporterAttributionText.FormatSupportedBy(" ", "zh-CN"),
        "Blank supporter names should not produce visible attribution text."
    );
}

static void TestEmptyInputReturnsNoSample()
{
    var sample = BPPSupporterSampler.Sample(Array.Empty<BPPSupporterEntry>(), Rolls(0f));

    AssertFalse(sample.HasValue, "Empty supporter input should not produce a sample.");
    AssertEqual(string.Empty, sample.Name, "Empty supporter input should return an empty name.");
    AssertEqual(0, sample.Tier, "Empty supporter input should return tier 0.");
}

static void TestInvalidEntriesAreIgnored()
{
    var entries = new[] { Entry("  ", 4), Entry("Zero Tier", 0), Entry("Valid Supporter", 2) };

    var sample = BPPSupporterSampler.Sample(entries, Rolls(0.99f, 0.99f));

    AssertTrue(
        sample.HasValue,
        "A valid entry should still be sampled when invalid entries exist."
    );
    AssertEqual(
        "Valid Supporter",
        sample.Name,
        "Sampler should ignore blank names and non-positive tiers."
    );
    AssertEqual(2, sample.Tier, "Sampler should preserve the selected supporter's tier.");
}

static void TestTierWeightsSelectDefaultBronzeSilverAndGoldBuckets()
{
    var entries = new[]
    {
        Entry("Default", 1),
        Entry("Bronze", 2),
        Entry("Silver", 3),
        Entry("Gold", 4),
    };

    AssertEqual(
        "Default",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0f)).Name,
        "The default tier occupies the first 1/15 of the tier roll."
    );
    AssertEqual(
        "Bronze",
        BPPSupporterSampler.Sample(entries, Rolls(1f / 15f + 0.001f, 0f)).Name,
        "Tier 2 should be selected after the default 1/15 range."
    );
    AssertEqual(
        "Silver",
        BPPSupporterSampler.Sample(entries, Rolls(3f / 15f + 0.001f, 0f)).Name,
        "Tier 3 should be selected after the default plus tier-2 ranges."
    );
    AssertEqual(
        "Gold",
        BPPSupporterSampler.Sample(entries, Rolls(6f / 15f + 0.001f, 0f)).Name,
        "Tier 4 should receive the final 9/15 range."
    );
}

static void TestTierBucketSelectionUsesUniformIndexRanges()
{
    var entries = new[] { Entry("Alice", 4), Entry("Bob", 4), Entry("Cara", 4) };

    AssertEqual(
        "Alice",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0f)).Name,
        "The first third of a tier bucket should select the first entry."
    );
    AssertEqual(
        "Bob",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0.34f)).Name,
        "The second third of a tier bucket should select the second entry."
    );
    AssertEqual(
        "Cara",
        BPPSupporterSampler.Sample(entries, Rolls(0f, 0.67f)).Name,
        "The final third of a tier bucket should select the third entry."
    );
}

static void TestSampleManyReturnsDistinctSupporters()
{
    var entries = new[]
    {
        Entry("Alice", 4),
        Entry("Bob", 4),
        Entry("Cara", 4),
        Entry("Dora", 4),
        Entry("Alice", 4),
    };

    var samples = BPPSupporterSampler.SampleMany(entries, 4, startIndex: 0, shuffleSeed: 17);

    AssertEqual(
        4,
        samples.Count,
        "SampleMany should return the requested count when enough unique names exist."
    );
    AssertEqual(
        4,
        samples.Select(sample => sample.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        "SampleMany should not repeat supporter names in one attribution row."
    );
    AssertTrue(
        samples.All(sample => sample.Tier == 4),
        "SampleMany should preserve supporter tiers."
    );
}

static void TestSampleManyRotatesThroughEverySupporterBeforeRepeating()
{
    var entries = new[]
    {
        Entry("Alice", 4),
        Entry("Bob", 3),
        Entry("Cara", 2),
        Entry("Dora", 1),
        Entry("Evan", 4),
    };

    var first = BPPSupporterSampler.SampleMany(entries, 2, startIndex: 0, shuffleSeed: 23);
    var second = BPPSupporterSampler.SampleMany(entries, 2, startIndex: 2, shuffleSeed: 23);
    var third = BPPSupporterSampler.SampleMany(entries, 2, startIndex: 4, shuffleSeed: 23);
    var names = first.Concat(second).Concat(third).Take(5).Select(sample => sample.Name).ToList();

    AssertEqual(
        5,
        names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        "Pseudo-random supporter sampling should rotate through the whole shuffled bag before repeating names."
    );
}

static void TestSampleManyDispersesLongNamesAcrossAttributionWindows()
{
    var entries = LongNameWindowEntries();
    const int rowSize = 4;

    for (var startIndex = 0; startIndex <= entries.Length - rowSize; startIndex++)
    {
        var samples = BPPSupporterSampler.SampleMany(entries, rowSize, startIndex, shuffleSeed: 0);

        AssertEqual(
            rowSize,
            samples.Count,
            "Long-name dispersal should still fill the attribution row when enough short names exist."
        );
        AssertTrue(
            samples.Count(sample => IsLongSupporterName(sample.Name)) <= 1,
            $"Attribution window starting at {startIndex} should include at most one long supporter name."
        );
    }
}

static void TestSampleManyLongNameDispersalKeepsFullRotation()
{
    var entries = LongNameWindowEntries();
    var first = BPPSupporterSampler.SampleMany(entries, 4, startIndex: 0, shuffleSeed: 0);
    var second = BPPSupporterSampler.SampleMany(entries, 4, startIndex: 4, shuffleSeed: 0);
    var third = BPPSupporterSampler.SampleMany(entries, 4, startIndex: 8, shuffleSeed: 0);
    var names = first.Concat(second).Concat(third).Select(sample => sample.Name).ToList();

    AssertEqual(
        entries.Length,
        names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        "Long-name dispersal should keep the shuffled-bag rotation contract intact."
    );
}

static void TestFixedSupporterListSourceIgnoresRemoteEntries()
{
    var remote = new[] { Entry("Remote Supporter", 4) };
    var fallback = new[] { Entry("Fallback Supporter", 2) };

    var entries = BPPSupporterListSourcePolicy.ResolveEntries(
        useFixedList: true,
        currentEntries: remote,
        fallbackEntries: fallback
    );

    AssertTrue(
        ReferenceEquals(entries, BPPSupporterFixedList.Entries),
        "Fixed supporter list mode should return the bundled fixed list."
    );
    AssertFalse(
        entries.Any(entry => entry.Name == "Remote Supporter"),
        "Fixed supporter list mode should ignore remote/cache entries."
    );
    AssertFalse(
        entries.Any(entry => entry.Name == "Fallback Supporter"),
        "Fixed supporter list mode should ignore fallback entries."
    );
}

static void TestDefaultSupporterListSourceUsesRemoteEntriesBeforeFallback()
{
    var remote = new[] { Entry("Remote Supporter", 4) };
    var fallback = new[] { Entry("Fallback Supporter", 2) };

    var entries = BPPSupporterListSourcePolicy.ResolveEntries(
        useFixedList: false,
        currentEntries: remote,
        fallbackEntries: fallback
    );

    AssertEqual(1, entries.Count, "Remote/cache entries should win in default mode.");
    AssertEqual(
        "Remote Supporter",
        entries[0].Name,
        "Default mode should preserve remote entries."
    );
}

static void TestSupportedByPrefixAndSuffix()
{
    AssertEqual(
        "Supported by",
        BPPSupporterAttributionText.FormatSupportedByPrefix("en"),
        "English attribution row should start with Supported by."
    );
    AssertEqual(
        string.Empty,
        BPPSupporterAttributionText.FormatSupportedBySuffix("en"),
        "English attribution row should not need a suffix."
    );
    AssertEqual(
        "由",
        BPPSupporterAttributionText.FormatSupportedByPrefix("zh-CN"),
        "Chinese attribution row should start with 由."
    );
    AssertEqual(
        "支持",
        BPPSupporterAttributionText.FormatSupportedBySuffix("zh-CN"),
        "Chinese attribution row should end with 支持."
    );
}

static void TestSponsorActionText()
{
    AssertEqual(
        "Sponsor",
        BPPSupporterAttributionText.FormatSponsorAction("en"),
        "English attribution row should expose a sponsor action."
    );
    AssertEqual(
        "赞助",
        BPPSupporterAttributionText.FormatSponsorAction("zh-CN"),
        "Chinese attribution row should expose a localized sponsor action."
    );
}

static void TestSponsorLinks()
{
    AssertEqual(
        "https://bazaarplusplus.com",
        BPPSupporterLinks.ResolveSponsorUrl("zh-CN"),
        "Chinese sponsor URL should use the canonical no-query domain."
    );
    AssertEqual(
        "https://bazaarplusplus.com",
        BPPSupporterLinks.ResolveSponsorUrl("zh-Hant"),
        "Traditional Chinese sponsor URL should use the canonical no-query domain."
    );
    AssertEqual(
        "https://bazaarplusplus.com/?lang=en",
        BPPSupporterLinks.ResolveSponsorUrl("en"),
        "English sponsor URL should force the English site."
    );
    AssertEqual(
        "https://bazaarplusplus.com/?lang=en",
        BPPSupporterLinks.ResolveSponsorUrl("de-DE"),
        "Non-Chinese sponsor URL should force the English site."
    );
    AssertEqual(
        "https://bazaarplusplus.com/?lang=en",
        BPPSupporterLinks.ResolveSponsorUrl(string.Empty),
        "Unknown language should fall back to English."
    );
    AssertFalse(
        BPPSupporterLinks.ResolveSponsorUrl("en").Contains("?lang=en?lang=en"),
        "Sponsor URL should not append duplicate lang parameters."
    );
}

static BPPSupporterEntry Entry(string name, int tier) => new() { Name = name, Tier = tier };

static BPPSupporterEntry[] LongNameWindowEntries() =>
    new[]
    {
        Entry("Alexandria", 4),
        Entry("Bob", 3),
        Entry("Cleo", 2),
        Entry("Dominique", 4),
        Entry("Eli", 1),
        Entry("Fay", 4),
        Entry("Gus", 3),
        Entry("Hiro", 2),
        Entry("Isabella", 4),
        Entry("Jay", 1),
        Entry("Kai", 4),
        Entry("Lia", 3),
    };

static bool IsLongSupporterName(string name) => name.Trim().Length > 7;

static Func<float> Rolls(params float[] values)
{
    var index = 0;
    return () =>
    {
        if (index >= values.Length)
            throw new InvalidOperationException("Test roll provider was exhausted.");

        return values[index++];
    };
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
