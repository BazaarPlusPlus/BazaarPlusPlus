using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;

AssertEqual(
    true,
    CollectionStagingTools.IsEnabled("1.0.11884-staging-windows-x64-0fff95cf"),
    "Staging builds should expose the collection template-ID copy tool."
);
AssertEqual(
    false,
    CollectionStagingTools.IsEnabled("1.0.11884-windows-x64-0fff95cf"),
    "Online builds must not expose the collection template-ID copy tool."
);
AssertEqual(
    false,
    CollectionStagingTools.IsEnabled("1.0.11884-ptr-windows-x64-0fff95cf"),
    "PTR builds must not expose the collection template-ID copy tool."
);

var mergedTierText = CollectionTierTooltipTextMerger.Merge(
    new[]
    {
        new CollectionTierTooltipText(ETier.Silver, "减速 <color=#C58F63>1</color> 件物品 1 秒"),
        new CollectionTierTooltipText(ETier.Gold, "减速 <color=#C58F63>2</color> 件物品 1 秒"),
        new CollectionTierTooltipText(ETier.Diamond, "减速 <color=#C58F63>3</color> 件物品 1 秒"),
    }
);

var mergedCooldownText = CollectionTierTooltipTextMerger.MergeCooldown(
    new[]
    {
        new CollectionTierTooltipText(ETier.Bronze, "10"),
        new CollectionTierTooltipText(ETier.Silver, "8"),
        new CollectionTierTooltipText(ETier.Gold, "7"),
        new CollectionTierTooltipText(ETier.Diamond, "5"),
    }
);
AssertEqual(
    "<size=36%><color=#B46241>10</color>><color=#C0C0C0>8</color>><color=#FFD700>7</color>><color=#00FFFF>5</color></size>",
    mergedCooldownText,
    "Cooldown tier text should use compact values and a clock-sized font."
);
AssertEqual(
    "减速 <color=#C58F63><color=#C0C0C0>1</color> <sprite name=Fusion> <color=#FFD700>2</color> <sprite name=Fusion> <color=#00FFFF>3</color></color> 件物品 1 秒",
    mergedTierText,
    "Tier tooltip text should keep common copy once and merge only changed values with tier colors."
);
AssertEqual(
    "冷却 4.0 秒",
    CollectionTierTooltipTextMerger.Merge(
        new[]
        {
            new CollectionTierTooltipText(ETier.Silver, "冷却 4.0 秒"),
            new CollectionTierTooltipText(ETier.Gold, "冷却 4.0 秒"),
            new CollectionTierTooltipText(ETier.Diamond, "冷却 4.0 秒"),
        }
    ),
    "Values shared by every available tier should render only once."
);

var retainedA = Guid.NewGuid();
var removedB = Guid.NewGuid();
var retainedC = Guid.NewGuid();
var addedD = Guid.NewGuid();
var retention = CollectionGridRetentionPlan.Build(
    new Dictionary<int, Guid>
    {
        [0] = retainedA,
        [1] = removedB,
        [2] = retainedC,
    },
    new[] { retainedC, retainedA, addedD }
);
AssertEqual(2, retention.Count, "Only cards present in both grids should be retained.");
AssertEqual(1, retention[0], "A retained card should map to its new visible index.");
AssertEqual(0, retention[2], "Reordered retained cards should map by stable card ID.");
AssertFalse(retention.ContainsKey(1), "Filtered-out cards should not be retained.");

// --- Skill grid: SkillColumns-wide square array, ceil(count / SkillColumns) shelves ---
var skills = Make(ECardType.Skill, 19, _ => ECardSize.Medium);
var skillLayout = CollectionGridLayout.Build(skills, CollectionTabKind.Skills);

AssertEqual(19, skillLayout.Count, "Skill layout keeps every card.");
AssertEqual(7, skillLayout.Columns, "Skill grid is 7 columns.");
AssertEqual(1, skillLayout.ShelfHeightUnits, "Skill shelves are one unit tall.");
AssertEqual(3, skillLayout.ShelfCount, "19 skills pack into ceil(19/7) = 3 shelves.");
AssertEqual(3, skillLayout.TotalRowUnits, "Skill total row-units == shelf count.");

AssertCell(skillLayout.CellAt(0), 0, 0, 1, "First skill is col0 / shelf0.");
AssertCell(skillLayout.CellAt(6), 6, 0, 1, "Seventh skill fills the first shelf.");
AssertCell(skillLayout.CellAt(7), 0, 1, 1, "Eighth skill wraps to col0 / shelf1.");
AssertCell(skillLayout.CellAt(18), 4, 2, 1, "Last skill is col4 / shelf2.");

AssertShelf(skillLayout.ShelfAt(0), 0, 6, "Skill shelf 0 covers indices 0..6.");
AssertShelf(skillLayout.ShelfAt(2), 14, 18, "Skill shelf 2 covers the tail 14..18.");

// --- Item grid: span-aware shelf packing (small 1, medium 2, large 3; all 2 units tall) ---
var trio = new[] { Item(ECardSize.Small), Item(ECardSize.Small), Item(ECardSize.Medium) };
var trioLayout = CollectionGridLayout.Build(trio, CollectionTabKind.Items);

AssertEqual(1, trioLayout.ShelfCount, "[S,S,M] fits one shelf (1+1+2 = 4 <= 10 units).");
AssertEqual(2, trioLayout.ShelfHeightUnits, "Item shelves are two units tall.");
AssertEqual(2, trioLayout.TotalRowUnits, "One item shelf == 2 row-units.");
AssertCell(trioLayout.CellAt(0), 0, 0, 1, "Small item spans 1 unit at col0.");
AssertCell(trioLayout.CellAt(1), 1, 0, 1, "Second small item at col1.");
AssertCell(trioLayout.CellAt(2), 2, 0, 2, "Medium item spans 2 units at col2.");

// Wrap: four mediums take 8 of 10 columns, the large (3) can't fit the last 2 so it wraps.
var wrap = new[]
{
    Item(ECardSize.Medium),
    Item(ECardSize.Medium),
    Item(ECardSize.Medium),
    Item(ECardSize.Medium),
    Item(ECardSize.Large),
};
var wrapLayout = CollectionGridLayout.Build(wrap, CollectionTabKind.Items);
AssertEqual(2, wrapLayout.ShelfCount, "Large wraps onto a second shelf.");
AssertCell(wrapLayout.CellAt(3), 6, 0, 2, "Fourth medium ends the first shelf at col6.");
AssertCell(wrapLayout.CellAt(4), 0, 1, 3, "Large wraps to col0 / shelf1 spanning 3.");
AssertShelf(wrapLayout.ShelfAt(0), 0, 3, "Item shelf 0 covers the four mediums.");
AssertShelf(wrapLayout.ShelfAt(1), 4, 4, "Item shelf 1 holds the wrapped large.");

// Exact fit: three larges (3 * 3 = 9 <= 10) share a shelf; a fourth wraps.
var twoLarge = CollectionGridLayout.Build(
    new[] { Item(ECardSize.Large), Item(ECardSize.Large) },
    CollectionTabKind.Items
);
AssertEqual(1, twoLarge.ShelfCount, "Two larges share one shelf (6 <= 10).");
AssertCell(twoLarge.CellAt(1), 3, 0, 3, "Second large sits at col3.");

var threeLarge = CollectionGridLayout.Build(
    new[] { Item(ECardSize.Large), Item(ECardSize.Large), Item(ECardSize.Large) },
    CollectionTabKind.Items
);
AssertEqual(1, threeLarge.ShelfCount, "Three larges share one shelf (9 <= 10).");
AssertCell(threeLarge.CellAt(2), 6, 0, 3, "Third large sits at col6.");

var fourLarge = CollectionGridLayout.Build(
    new[]
    {
        Item(ECardSize.Large),
        Item(ECardSize.Large),
        Item(ECardSize.Large),
        Item(ECardSize.Large),
    },
    CollectionTabKind.Items
);
AssertEqual(2, fourLarge.ShelfCount, "Fourth large wraps (9 + 3 > 10).");
AssertCell(fourLarge.CellAt(3), 0, 1, 3, "Fourth large wraps to col0 / shelf1.");

// --- Pixelization (unit = 100, gap = 10, originX = 18, originY = 18; step = 110) ---
AssertRect(
    skillLayout.ContentRectFor(9, 100f, 10f, 18f, 18f),
    238f,
    128f,
    100f,
    100f,
    "Skill col2/shelf1 → (238,128,100,100)."
);
AssertRect(
    trioLayout.ContentRectFor(2, 100f, 10f, 18f, 18f),
    238f,
    18f,
    210f,
    210f,
    "Item medium col2/shelf0 → (238,18,210,210)."
);
AssertRect(
    wrapLayout.ContentRectFor(4, 100f, 10f, 18f, 18f),
    18f,
    238f,
    320f,
    210f,
    "Item large col0/shelf1 → (18,238,320,210)."
);

AssertApprox(440f, wrapLayout.ContentHeight(100f, 10f), "Two item shelves → 4 * 110 = 440 tall.");
AssertApprox(
    330f,
    skillLayout.ContentHeight(100f, 10f),
    "Three skill shelves → 3 * 110 = 330 tall."
);
AssertApprox(220f, wrapLayout.ShelfPitch(100f, 10f), "Item shelf pitch is 2 * 110.");
AssertApprox(110f, skillLayout.ShelfPitch(100f, 10f), "Skill shelf pitch is 1 * 110.");

// --- Pixelization keeps the grid envelope stable across Item / Skill tabs ---
var wideViewport = 2600f;
var itemPixels = CollectionGridPixelization.ForViewport(
    wideViewport,
    CollectionGridConstants.ItemColumns
);
var skillPixels = CollectionGridPixelization.ForViewport(
    wideViewport,
    CollectionGridConstants.SkillColumns
);

AssertApprox(
    itemPixels.GridWidth,
    skillPixels.GridWidth,
    "Item and Skill grids should occupy the same total width in the preview area."
);
AssertApprox(
    itemPixels.OriginX,
    skillPixels.OriginX,
    "Item and Skill grids should share the same horizontal origin in the preview area."
);

// --- Material cache LRU: evicts only unreferenced materials and stays bounded when possible ---
var materialLru = new CollectionCardMaterialLru(capacity: 2);
AssertValues(
    materialLru.Acquire("a"),
    Array.Empty<string>(),
    "First material acquire should not evict."
);
materialLru.Release("a");
materialLru.Acquire("b");
materialLru.Release("b");
AssertValues(
    materialLru.Acquire("c"),
    new[] { "a" },
    "LRU should evict the oldest unreferenced material when capacity is exceeded."
);
AssertFalse(materialLru.Contains("a"), "Evicted material key should leave the LRU.");
AssertTrue(materialLru.Contains("b"), "Newer unreferenced material should stay resident.");
AssertTrue(materialLru.Contains("c"), "Newest material should stay resident.");

var referencedLru = new CollectionCardMaterialLru(capacity: 2);
referencedLru.Acquire("active-a");
referencedLru.Acquire("idle-b");
referencedLru.Release("idle-b");
AssertValues(
    referencedLru.Acquire("active-c"),
    new[] { "idle-b" },
    "LRU should skip referenced entries and evict an idle entry instead."
);
AssertTrue(referencedLru.Contains("active-a"), "Referenced oldest material should not be evicted.");
AssertTrue(referencedLru.Contains("active-c"), "New referenced material should remain tracked.");

var allReferencedLru = new CollectionCardMaterialLru(capacity: 1);
allReferencedLru.Acquire("active-a");
AssertValues(
    allReferencedLru.Acquire("active-b"),
    Array.Empty<string>(),
    "LRU should temporarily exceed capacity rather than evict a referenced material."
);
AssertEqual(
    2,
    allReferencedLru.Count,
    "All-referenced material cache should report the temporary over-capacity state."
);
allReferencedLru.Release("active-a");
AssertValues(
    allReferencedLru.Acquire("active-c"),
    new[] { "active-a" },
    "Once an old material is released, a later acquire should evict it."
);

// --- Degenerate: empty visible set ---
var empty = CollectionGridLayout.Build(
    System.Array.Empty<CollectionCardVm>(),
    CollectionTabKind.Items
);
AssertEqual(0, empty.Count, "Empty layout has no cells.");
AssertEqual(0, empty.ShelfCount, "Empty layout has no shelves.");
AssertApprox(0f, empty.ContentHeight(100f, 10f), "Empty layout is zero tall.");

// --- CollectionCardFitMath: scale + position (Unity-free pure rules) ---
// Large outer-frame clamp: measured width includes flourishes wider than the body. Without the
// body-width clamp (aspect-derived), frame-wide maxWidth would shrink Large cards shorter than
// Medium; with it, scale stays height-driven when body fits, and only clamps when body overflows.
const float frameHeightOverSocket = 1.03704f;
const float gap = 14f;
const float inset = CollectionGridConstants.CellContentInset;
var largeCell = new CollectionGridRect(x: 0f, y: 0f, width: 300f, height: 200f);

// Frame flourishes make measured width wide while body (aspect * bodyH) is narrower and fits.
var largeFrameBounds = new CardVisualBounds(width: 600f, height: 310f, centerX: 0f, centerY: 0f);
const float largeBodyAspect = 1.5f; // 3:2 body
var largeScale = CollectionCardFitMath.ComputeScale(
    largeFrameBounds,
    largeBodyAspect,
    largeCell,
    gap,
    inset,
    frameHeightOverSocket
);
var largeTargetH = System.Math.Max(1f, largeCell.Height * (1f - 2f * inset));
var largeHeightDriven = largeTargetH / largeFrameBounds.Height;
AssertApprox(
    largeHeightDriven,
    largeScale,
    "Large with body-aspect should keep height-driven scale when body width fits maxWidth."
);

// Same measured frame without aspect falls back to natW and clamps (frame wider than cell+gap).
var frameClampedScale = CollectionCardFitMath.ComputeScale(
    largeFrameBounds,
    aspectRatio: null,
    largeCell,
    gap,
    inset,
    frameHeightOverSocket
);
AssertTrue(
    frameClampedScale < largeHeightDriven - 0.001f,
    "Without aspect, frame-width clamp must shrink scale below height-driven."
);
AssertApprox(
    (largeCell.Width + gap) / largeFrameBounds.Width,
    frameClampedScale,
    "Null aspect should clamp by measured visual width against cellWidth+gap."
);

// Degenerate aspectRatio falls back to measured width (same as null).
foreach (var badAspect in new[] { float.NaN, float.PositiveInfinity, 0f, 0.01f, -1f })
{
    var scale = CollectionCardFitMath.ComputeScale(
        largeFrameBounds,
        badAspect,
        largeCell,
        gap,
        inset,
        frameHeightOverSocket
    );
    AssertApprox(
        frameClampedScale,
        scale,
        $"Degenerate aspectRatio {badAspect} should fall back to measured-width clamp."
    );
}

// Zero / negative / non-finite visual height poisons targetH/natH → final scale guard returns 1f.
foreach (
    var badHeight in new[] { 0f, -10f, float.NaN, float.PositiveInfinity, float.NegativeInfinity }
)
{
    var scale = CollectionCardFitMath.ComputeScale(
        new CardVisualBounds(100f, badHeight, 0f, 0f),
        aspectRatio: 1.5f,
        largeCell,
        gap,
        inset,
        frameHeightOverSocket
    );
    AssertApprox(1f, scale, $"Visual height {badHeight} must guard scale to 1f.");
}

// Body overflow clamp: narrow cell forces body-width clamp even with usable aspect.
var narrowCell = new CollectionGridRect(x: 0f, y: 0f, width: 80f, height: 200f);
var overflowBounds = new CardVisualBounds(width: 200f, height: 200f, centerX: 0f, centerY: 0f);
var overflowScale = CollectionCardFitMath.ComputeScale(
    overflowBounds,
    aspectRatio: 2f,
    narrowCell,
    gap,
    inset,
    frameHeightOverSocket
);
var bodyH = overflowBounds.Height / frameHeightOverSocket;
var bodyW = 2f * bodyH;
var expectedOverflow = (narrowCell.Width + gap) / bodyW;
AssertApprox(
    expectedOverflow,
    overflowScale,
    "When body*heightScale exceeds maxWidth, scale must clamp to maxWidth/bodyW."
);

// Position: center-align with zero scroll and measured visual center at origin.
var posCell = new CollectionGridRect(x: 100f, y: 40f, width: 200f, height: 100f);
var centeredBounds = new CardVisualBounds(width: 50f, height: 50f, centerX: 0f, centerY: 0f);
var (px, py) = CollectionCardFitMath.ComputeAnchoredPosition(
    centeredBounds,
    scaleX: 0.5f,
    scaleY: 0.5f,
    posCell,
    scrollY: 0f
);
AssertApprox(200f, px, "Zero visual center + scale should place root at cell center X.");
AssertApprox(-(40f + 50f), py, "Zero visual center should place root at cell center Y (negated).");

// Scroll offset: content Y shifts up by scrollY, so screenTop drops and target Y rises.
var (pxScrolled, pyScrolled) = CollectionCardFitMath.ComputeAnchoredPosition(
    centeredBounds,
    scaleX: 0.5f,
    scaleY: 0.5f,
    posCell,
    scrollY: 20f
);
AssertApprox(px, pxScrolled, "Scroll should not move horizontal anchor.");
AssertApprox(py + 20f, pyScrolled, "ScrollY should shift anchored Y by +scrollY.");

// Non-zero visual center offsets placement by center*scale.
var offsetBounds = new CardVisualBounds(width: 50f, height: 50f, centerX: 10f, centerY: -4f);
var (pxOff, pyOff) = CollectionCardFitMath.ComputeAnchoredPosition(
    offsetBounds,
    scaleX: 2f,
    scaleY: 3f,
    posCell,
    scrollY: 0f
);
AssertApprox(200f - 10f * 2f, pxOff, "Anchored X subtracts centerX * scaleX.");
AssertApprox(-(40f + 50f) - (-4f) * 3f, pyOff, "Anchored Y subtracts centerY * scaleY.");

// --- NativeCardCellBoundsCache: hit/miss matrix for the three invalidation hooks ---
// Store once, then assert each scroll-style read hits and each named hook clears.
var boundsCache = new NativeCardCellBoundsCache();
AssertFalse(boundsCache.IsValid, "Fresh cache must start empty.");
AssertFalse(
    boundsCache.TryGet(out _, out _),
    "TryGet on empty cache must miss (scroll path would skip measure)."
);

var storedBounds = new CardVisualBounds(width: 120f, height: 200f, centerX: 1f, centerY: -2f);
const float storedAspect = 1.5f;
boundsCache.Store(storedBounds, storedAspect);
AssertTrue(boundsCache.IsValid, "Store must mark cache valid.");
AssertTrue(boundsCache.TryGet(out var hitBounds, out var hitAspect), "Warm cache must hit.");
AssertApprox(storedBounds.Width, hitBounds.Width, "Hit returns stored width.");
AssertApprox(storedBounds.Height, hitBounds.Height, "Hit returns stored height.");
AssertApprox(storedBounds.CenterX, hitBounds.CenterX, "Hit returns stored centerX.");
AssertApprox(storedBounds.CenterY, hitBounds.CenterY, "Hit returns stored centerY.");
AssertEqual(storedAspect, hitAspect, "Hit returns stored aspectRatio.");

// Scroll-style reads (no invalidation) stay warm across many frames.
for (var i = 0; i < 5; i++)
{
    AssertTrue(
        boundsCache.TryGet(out _, out _),
        $"Scroll frame {i} must hit warm cache (zero measure)."
    );
}

// Hook matrix: each invalidation clears, re-store restores, other hooks independently clear.
var hooks = new (string name, System.Action invalidate)[]
{
    ("InvalidateOnRebind", () => boundsCache.InvalidateOnRebind()),
    ("InvalidateOnScaleDirty", () => boundsCache.InvalidateOnScaleDirty()),
    ("InvalidateOnArtLoaded", () => boundsCache.InvalidateOnArtLoaded()),
};
foreach (var (name, invalidate) in hooks)
{
    boundsCache.Store(storedBounds, storedAspect);
    AssertTrue(boundsCache.IsValid, $"{name}: pre-condition Store must warm cache.");
    AssertTrue(boundsCache.TryGet(out _, out _), $"{name}: pre-condition TryGet must hit.");

    invalidate();
    AssertFalse(boundsCache.IsValid, $"{name} must clear IsValid.");
    AssertFalse(boundsCache.TryGet(out _, out _), $"{name} must make subsequent TryGet miss.");

    // After miss, Store restores a hit (simulates force remeasure on that hook).
    boundsCache.Store(storedBounds, storedAspect);
    AssertTrue(boundsCache.TryGet(out _, out _), $"{name}: re-Store after invalidate must hit.");
}

// Cross-hook: invalidate A, hit remains false until Store; invalidate B after re-Store still clears.
boundsCache.Store(storedBounds, storedAspect);
boundsCache.InvalidateOnRebind();
AssertFalse(boundsCache.TryGet(out _, out _), "Rebind miss must not be restored by other hooks.");
boundsCache.Store(storedBounds, storedAspect);
boundsCache.InvalidateOnScaleDirty();
AssertFalse(boundsCache.TryGet(out _, out _), "ScaleDirty after re-Store must miss.");
boundsCache.Store(storedBounds, storedAspect);
boundsCache.InvalidateOnArtLoaded();
AssertFalse(boundsCache.TryGet(out _, out _), "ArtLoaded after re-Store must miss.");

// Null aspect is a valid stored value (skills / no AspectRatioFitter).
boundsCache.Store(storedBounds, aspectRatio: null);
AssertTrue(boundsCache.TryGet(out _, out var nullAspect), "Null aspect Store must hit.");
AssertEqual(null, nullAspect, "Stored null aspect must round-trip.");

System.Console.WriteLine("CollectionGridLayout checks passed.");

static CollectionCardVm Item(ECardSize size) => new() { Type = ECardType.Item, Size = size };

static CollectionCardVm[] Make(ECardType type, int count, System.Func<int, ECardSize> size)
{
    var list = new CollectionCardVm[count];
    for (var i = 0; i < count; i++)
        list[i] = new CollectionCardVm { Type = type, Size = size(i) };
    return list;
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
}

static void AssertCell(CollectionGridCell cell, int col, int shelf, int widthSpan, string message)
{
    if (cell.Col != col || cell.Shelf != shelf || cell.WidthSpan != widthSpan)
        throw new InvalidOperationException(
            $"{message} Expected: (col {col}, shelf {shelf}, span {widthSpan}), "
                + $"Actual: (col {cell.Col}, shelf {cell.Shelf}, span {cell.WidthSpan})"
        );
}

static void AssertShelf(CollectionGridShelf shelf, int first, int last, string message)
{
    if (shelf.FirstIndex != first || shelf.LastIndex != last)
        throw new InvalidOperationException(
            $"{message} Expected: [{first}..{last}], Actual: [{shelf.FirstIndex}..{shelf.LastIndex}]"
        );
}

static void AssertRect(
    CollectionGridRect rect,
    float x,
    float y,
    float width,
    float height,
    string message
)
{
    if (
        !Approx(rect.X, x)
        || !Approx(rect.Y, y)
        || !Approx(rect.Width, width)
        || !Approx(rect.Height, height)
    )
        throw new InvalidOperationException(
            $"{message} Expected: ({x},{y},{width},{height}), "
                + $"Actual: ({rect.X},{rect.Y},{rect.Width},{rect.Height})"
        );
}

static void AssertApprox(float expected, float actual, string message)
{
    if (!Approx(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

static void AssertValues<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string message)
{
    if (actual.Count != expected.Count)
        throw new InvalidOperationException(
            $"{message} Expected {expected.Count} values, got {actual.Count}."
        );
    for (var i = 0; i < actual.Count; i++)
    {
        if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i]))
            throw new InvalidOperationException(
                $"{message} At {i}: expected {expected[i]}, got {actual[i]}."
            );
    }
}

static bool Approx(float a, float b) => System.Math.Abs(a - b) < 0.001f;
