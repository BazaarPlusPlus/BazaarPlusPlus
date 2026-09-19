using BazaarPlusPlus.Game.BilingualItemNames;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;

TestUnicodeFontCoverage();
TestNativeGameFontSelection();
TestBilingualItemNamePresentation();
TestStablePanelTextCompactionKeepsStableSlots();
TestCollectionSortWidthsKeepStableGroup();

Console.WriteLine("UiFoundation checks passed.");

static void TestUnicodeFontCoverage()
{
    Assert(
        UnicodeFontCoverage.ContainsCjk("由 Alice 支持"),
        "CJK detection should include mixed Chinese text."
    );
    Assert(UnicodeFontCoverage.ContainsCjk("㐀"), "CJK detection should include Extension A.");
    Assert(
        !UnicodeFontCoverage.ContainsCjk("Supported by Alice"),
        "CJK detection should leave pure Latin text alone."
    );

    var missing = UnicodeFontCoverage.TryFindMissingCodePoint(
        "A俱B",
        character => character != '俱',
        out var missingCodePoint
    );
    Assert(missing && missingCodePoint == '俱', "BMP coverage should report the missing glyph.");

    missing = UnicodeFontCoverage.TryFindMissingCodePoint("A𠀀B", _ => true, out missingCodePoint);
    Assert(
        missing && missingCodePoint == 0x20000,
        "Supplementary-plane text should be rejected with the complete code point."
    );
}

static void TestNativeGameFontSelection()
{
    var candidates = new[]
    {
        new FontCandidate("Static", false),
        new FontCandidate("SecondSet", false),
        new FontCandidate("Dynamic", true),
    };
    Assert(
        NativeGameFontSelection.FindLastIndexWithSource(candidates, item => item.HasSource) == 2,
        "The adapter must select the Dynamic asset with a source font."
    );
    Assert(
        NativeGameFontSelection.FindLastIndexWithSource(candidates[..2], item => item.HasSource)
            == -1,
        "Static and SecondSet assets must not be treated as a source font."
    );
    Assert(
        !NativeGameFontSelection.HasCompleteChain(candidates.Length, candidates.Length - 1),
        "A partial Static/SecondSet chain must not be accepted when Dynamic failed to load."
    );
    Assert(
        NativeGameFontSelection.HasCompleteChain(candidates.Length, candidates.Length),
        "The complete Static/SecondSet/Dynamic chain should be accepted."
    );
}

static void TestBilingualItemNamePresentation()
{
    Assert(
        BilingualItemNamePresentation.TryBuildSubtitle("Lighter", "打火机") == "打火机",
        "Enabled item tooltips should expose the trimmed translated title."
    );
    Assert(
        BilingualItemNamePresentation.TryBuildSubtitle("打火机", "Lighter") == "Lighter",
        "Chinese clients should expose the authored English title."
    );
}

static void TestStablePanelTextCompactionKeepsStableSlots()
{
    Assert(
        StablePanelText.Compact("Short status", 32) == "Short status",
        "Short text should not be changed."
    );
    Assert(
        StablePanelText.Compact("Hidden status", 0) == string.Empty,
        "Non-positive budgets should hide the text instead of expanding the slot."
    );
    Assert(
        StablePanelText.Compact("Line one\n\tline two   line three", 64)
            == "Line one line two line three",
        "Medium text should collapse whitespace without truncating."
    );

    var longMessage =
        "Network refresh failed because the upstream endpoint returned a long diagnostic payload "
        + "with repeated retry metadata and a platform-specific file path that would otherwise "
        + "grow the panel footer.";
    var compact = StablePanelText.Compact(longMessage, 80);
    Assert(compact.Length <= 80, "Long text should stay within the caller's character budget.");
    Assert(
        compact.EndsWith("...", StringComparison.Ordinal),
        "Long text should advertise truncation."
    );
    Assert(
        compact.StartsWith("Network refresh failed", StringComparison.Ordinal),
        "Long text should keep the actionable prefix visible."
    );

    var unbroken = new string('x', 300);
    Assert(
        StablePanelText.Compact(unbroken, 48).Length == 48,
        "Unbroken text should also be clamped."
    );
}

static void TestCollectionSortWidthsKeepStableGroup()
{
    Assert(
        Sizes.CollectionSortActiveWidth > Sizes.CollectionSortInactiveWidth,
        "Chinese CollectionPanel sort buttons should give the selected segment more room."
    );
    Assert(
        Sizes.CollectionSortEnglishActiveWidth > Sizes.CollectionSortEnglishInactiveWidth,
        "English CollectionPanel sort buttons should give the selected segment more room."
    );
    Assert(
        Sizes.CollectionSortActiveWidth + Sizes.CollectionSortInactiveWidth
            == Sizes.CollectionSortEnglishActiveWidth + Sizes.CollectionSortEnglishInactiveWidth,
        "Changing language should preserve the total sort-group width."
    );
    Assert(
        Sizes.CollectionSortInactiveWidth >= Sizes.InfoChipMinWidth
            && Sizes.CollectionSortEnglishInactiveWidth >= Sizes.InfoChipMinWidth,
        "Inactive sort segments should not shrink below the compact chip floor."
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal readonly record struct FontCandidate(string Name, bool HasSource);
