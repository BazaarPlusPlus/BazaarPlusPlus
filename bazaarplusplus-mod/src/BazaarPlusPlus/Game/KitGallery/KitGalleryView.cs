#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.GameInterop.Ranks;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.KitGallery;

// The Kit Gallery's sheet: every Desktop UI kit control in every state, next to the game art BPP
// shows (hero portraits, trophies, rank badges), on History's canvas. The strings are fixed
// Simplified Chinese samples: the sheet is a Debug-build tool, not a player surface.
internal sealed class KitGalleryView : IDisposable
{
    private const float RailWidth = 440f;
    private const float RowHeight = 64f;
    private const float StateColumn = 112f;
    private const float LabelColumn = 84f;

    private static readonly string[] TrophyAddresses =
    {
        "Trophy_Bronze_TUI",
        "Trophy_Silver_TUI",
        "Trophy_Gold_TUI",
        "Trophy_Diamond_TUI",
    };

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly NativeRankBadgeProvider _ranks = new();
    private readonly List<(Image Target, int Hero)> _portraitTargets = new();
    private readonly List<(Image Target, int Tier)> _trophyTargets = new();
    private readonly List<(Image Target, string Rank)> _rankTargets = new();
    private readonly List<KitButton> _rows = new();
    private readonly Sprite?[] _heroes = new Sprite?[6];
    private readonly Sprite?[] _trophies = new Sprite?[TrophyAddresses.Length];
    private Task<HeroPortraitLoadOutcome?>[]? _heroLoad;
    private KitPortraitFocus?[]? _heroFocus;
    private Task<Sprite?[]>? _trophyLoad;
    private GameObject? _root;
    private DesktopKit? _kit;
    private TMPro.TextMeshProUGUI? _diagnostics;
    private int _selectedRow;
    private int _selectedDay = 6;
    private readonly List<KitButton> _days = new();
    private KitButton[] _segments = Array.Empty<KitButton>();

    internal KitGalleryView(Transform parent, Action close)
    {
        _parent = parent;
        _close = close;
    }

    internal void SetVisible(bool visible)
    {
        if (visible)
            EnsureCreated();
        if (_root != null)
            _root.SetActive(visible);
    }

    internal void Tick()
    {
        if (_root == null || !_root.activeSelf)
            return;
        _kit!.Tick();
        if (_ranks.Tick())
            foreach (var (target, rank) in _rankTargets)
                DesktopKit.Show(target, _ranks.Get(rank));
        LoadArt();
        if (_diagnostics != null)
            _diagnostics.text = Diagnostics();
    }

    private void EnsureCreated()
    {
        if (_root != null)
            return;
        if (
            NativeGameTypography.PrepareOwnedText(out var font)
            != NativeGameTypography.Outcome.Ready
        )
            return;
        _kit = new DesktopKit(font!);
        _root = new GameObject(
            "KitGallery",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _root.transform.SetParent(_parent, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = BppOverlaySorting.PanelUiToolkit;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 1000);
        scaler.matchWidthOrHeight = .5f;

        var ground = _root.AddComponent<Image>();
        ground.color = InGameTheme.Backdrop;
        var shell = _root.AddComponent<HorizontalLayoutGroup>();
        shell.childControlWidth = shell.childControlHeight = true;
        shell.childForceExpandWidth = false;
        shell.childForceExpandHeight = true;

        // The list is the first thing to read and the way into every other part, so its rail
        // is on the left; what the selected run shows is on the right.
        var rail = Column(_root.transform, "Rail", InGameSizes.Space12, 22, 24);
        _kit.Surface(rail, KitSurface.Ground);
        DesktopKit.Size(rail, RailWidth, -1);
        BuildRail(rail);

        var content = Column(_root.transform, "Content", InGameSizes.Space24, 32, 28);
        content.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        BuildContent(content);
    }

    // ---- the sheet ----

    private void BuildContent(RectTransform content)
    {
        var kit = _kit!;
        var top = Row(content, "Top", InGameSizes.Space24, TextAnchor.UpperLeft);

        var type = Column(top, "Type", InGameSizes.Space8);
        DesktopKit.Size(type, 236, -1);
        kit.SectionHeader(type, "文字层级", "字号 · 颜色");
        kit.Text(type, "对局历史", KitTextRole.Title);
        kit.Text(type, "对手阵容 · 18", KitTextRole.Section);
        kit.Text(type, "凡妮莎 · 第 7 天 · 16", KitTextRole.Body);
        kit.Text(type, "普通 · 26 分钟 · 14", KitTextRole.Meta);
        kit.Text(type, "第 1–5 局，共 23 局 · 12", KitTextRole.Caption);
        kit.Text(type, "6,424 套阵容", KitTextRole.Body, InGameTheme.Accent);

        var buttons = Column(top, "Buttons", InGameSizes.Space8);
        buttons.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        kit.SectionHeader(buttons, "按钮", "md 36 · sm 30");
        var head = Row(buttons, "Head", 0, TextAnchor.MiddleLeft);
        Cell(head, LabelColumn);
        foreach (var state in new[] { "常态", "悬停", "按下", "变暗", "不可用", "sm" })
            kit.Text(Cell(head, StateColumn), state, KitTextRole.Caption);
        ButtonRow(buttons, "主要", "回放", KitButtonKind.Primary);
        ButtonRow(buttons, "次要", "收藏", KitButtonKind.Secondary);
        ButtonRow(buttons, "弱化", "较早", KitButtonKind.Quiet);
        ButtonRow(buttons, "危险", "删除", KitButtonKind.Danger);
        var extra = Row(buttons, "Extra", 0, TextAnchor.MiddleLeft);
        kit.Text(Cell(extra, LabelColumn), "选中 · 图标", KitTextRole.Caption);
        var toggle = kit.Button(
            Cell(extra, StateColumn),
            "包含 Tiny",
            KitButtonKind.Secondary,
            KitSize.Md,
            () => { }
        );
        toggle.On = true;
        kit.IconButton(
            Cell(extra, StateColumn),
            KitIcon.Close,
            KitButtonKind.Secondary,
            KitSize.Md,
            () => { }
        );
        kit.IconButton(
            Cell(extra, StateColumn),
            KitIcon.Close,
            KitButtonKind.Secondary,
            KitSize.Sm,
            () => { }
        );
        kit.Button(
            Cell(extra, StateColumn * 2),
            "确认删除",
            KitButtonKind.DangerConfirm,
            KitSize.Md,
            () => { }
        );

        var middle = Row(content, "Middle", InGameSizes.Space24, TextAnchor.UpperLeft);
        var choose = Column(middle, "Choose", InGameSizes.Space12);
        DesktopKit.Size(choose, 520, -1);
        kit.SectionHeader(choose, "选择", "分段 · 标签 · 勾选");
        var segments = Row(choose, "Segments", InGameSizes.Space12, TextAnchor.MiddleLeft);
        var md = kit.Segmented(segments, new[] { "对局", "幽灵", "收藏" }, KitSize.Md, _ => { });
        DesktopKit.Size(md.Track, 300, -1);
        md.Options[0].On = true;
        var sm = kit.Segmented(segments, new[] { "物品", "技能" }, KitSize.Sm, _ => { });
        DesktopKit.Size(sm.Track, 150, -1);
        sm.Options[0].On = true;
        var chips = Row(choose, "Chips", InGameSizes.Space6, TextAnchor.MiddleLeft);
        Chip(chips, "武器", null, false);
        Chip(chips, "护盾", null, true);
        Chip(chips, "黄金", InGameTheme.TierGold, false);
        Chip(chips, "钻石", InGameTheme.TierDiamond, false);
        Chip(chips, "传奇", InGameTheme.TierLegendary, true);
        var checks = Row(choose, "Checks", InGameSizes.Space24, TextAnchor.MiddleLeft);
        KitButton? first = null;
        first = kit.Checkbox(checks, "只看十胜", () => first!.On = !first.On);
        KitButton? second = null;
        second = kit.Checkbox(checks, "包含 Tiny", () => second!.On = !second.On);
        second.On = true;

        var fields = Column(middle, "Fields", InGameSizes.Space12);
        fields.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        kit.SectionHeader(fields, "输入", "放在 ingame-panel 上");
        var well = Column(fields, "Well", InGameSizes.Space8, 14, 14);
        kit.Surface(well, KitSurface.Ground);
        kit.Select(well, "全部英雄", () => { });
        kit.Search(well, "搜索物品");

        var strip = Column(content, "Battles", InGameSizes.Space8);
        kit.SectionHeader(strip, "本局 12 场战斗", "10 胜 2 负 · 点选一场查看阵容");
        var days = kit.DayStrip(strip);
        DesktopKit.Size(days, -1, 92);
        for (var day = 0; day < 12; day++)
        {
            var index = day;
            var won = day is not (2 or 6);
            var button = kit.Day(
                days,
                $"第 {day + 1} 天",
                won ? KitMark.Won : KitMark.Lost,
                () => SelectDay(index),
                out var portrait
            );
            _portraitTargets.Add((portrait, day % _heroes.Length));
            _days.Add(button);
        }
        SelectDay(_selectedDay);

        var bottom = Row(content, "Bottom", InGameSizes.Space24, TextAnchor.UpperLeft);
        var tooltip = Column(bottom, "TooltipBlock", InGameSizes.Space8);
        DesktopKit.Size(tooltip, 300, -1);
        kit.SectionHeader(tooltip, "提示", "");
        var tip = kit.Floating(tooltip, "Tooltip", InGameSizes.Space12);
        kit.Text(tip, "火焰之剑", KitTextRole.Body);
        kit.Text(tip, "出现在 412 套十胜阵容里", KitTextRole.Meta);
        var dialogBlock = Column(bottom, "DialogBlock", InGameSizes.Space8);
        DesktopKit.Size(dialogBlock, 440, -1);
        kit.SectionHeader(dialogBlock, "确认对话框", "");
        var dialog = kit.Floating(dialogBlock, "Dialog", InGameSizes.Space16);
        kit.Text(dialog, "删除这局记录？", KitTextRole.Section);
        var body = kit.Text(dialog, "录像和幽灵数据会一起删除，不能恢复。", KitTextRole.Meta);
        body.textWrappingMode = TMPro.TextWrappingModes.Normal;
        var actions = Row(dialog, "Actions", InGameSizes.Space8, TextAnchor.MiddleRight);
        kit.Button(actions, "取消", KitButtonKind.Secondary, KitSize.Md, () => { });
        kit.Button(actions, "删除", KitButtonKind.DangerConfirm, KitSize.Md, () => { });
        var info = Column(bottom, "Diagnostics", InGameSizes.Space8);
        info.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        kit.SectionHeader(info, "诊断", "");
        _diagnostics = kit.Text(info, Diagnostics(), KitTextRole.Caption);
        _diagnostics.textWrappingMode = TMPro.TextWrappingModes.Normal;
    }

    private void BuildRail(RectTransform rail)
    {
        var kit = _kit!;
        var head = Row(rail, "Head", InGameSizes.Space8, TextAnchor.MiddleLeft);
        kit.Text(head, "组件库", KitTextRole.Title)
            .gameObject.AddComponent<LayoutElement>()
            .flexibleWidth = 1;
        kit.Button(head, "更多", KitButtonKind.Secondary, KitSize.Sm, () => { });
        kit.IconButton(head, KitIcon.Close, KitButtonKind.Secondary, KitSize.Sm, _close);
        var credit = Row(rail, "Credit", InGameSizes.Space8, TextAnchor.MiddleLeft);
        kit.Text(credit, "感谢", KitTextRole.Caption);
        kit.Text(credit, "LoganShi", KitTextRole.Caption, InGameTheme.Supporter1);
        kit.Text(credit, "酒菜纳泽", KitTextRole.Caption, InGameTheme.Supporter2);
        kit.Text(credit, "辰月", KitTextRole.Caption, InGameTheme.Supporter3);
        kit.Text(credit, "zheng50", KitTextRole.Caption, InGameTheme.Supporter4)
            .gameObject.AddComponent<LayoutElement>()
            .flexibleWidth = 1;
        Flush(credit, left: false);
        var sponsor = kit.Button(credit, "赞助", KitButtonKind.Quiet, KitSize.Sm, () => { });
        sponsor.InkOverride = InGameTheme.Supporter4;
        var tabs = kit.Segmented(rail, new[] { "对局", "幽灵", "收藏" }, KitSize.Sm, SelectSegment);
        _segments = tabs.Options;
        SelectSegment(0);
        kit.Select(rail, "全部英雄", () => { });
        kit.SectionHeader(rail, "最近对局", "6 局");
        var list = Column(rail, "Runs", 0);
        var gain = ColorUtility.ToHtmlStringRGB(InGameTheme.Success);
        var loss = ColorUtility.ToHtmlStringRGB(InGameTheme.Failure);
        RunRow(list, 0, "凡妮莎", "普通 · 12 场 · 10/27 19:23", null, 2, null, "10 胜", true);
        RunRow(
            list,
            1,
            "凡妮莎",
            "普通 · 3 场 · 10/27 17:29",
            null,
            -1,
            KitMark.Abandoned,
            "",
            false
        );
        RunRow(
            list,
            2,
            "杜利",
            $"钻石 1520 <color=#{gain}>+25</color> · 10/26",
            "Diamond",
            1,
            null,
            "7 胜",
            false
        );
        RunRow(
            list,
            3,
            "马克",
            $"黄金 1495 <color=#{loss}>−18</color> · 10/25",
            "Gold",
            -1,
            KitMark.Active,
            "",
            false
        );
        RunRow(list, 4, "朱尔斯", "普通 · 9 场 · 10/24 22:40", null, 0, null, "4 胜", false);
        RunRow(list, 5, "皮格", "白银 980 · 10/23", "Silver", -1, KitMark.Completed, "2 胜", false);
        SelectRow(0);
        var spacer = DesktopKit.Rect(rail, "Spacer");
        spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var pager = Row(rail, "Pager", InGameSizes.Space6, TextAnchor.MiddleLeft);
        Flush(pager, left: true);
        kit.IconLabelButton(
            pager,
            "",
            KitIcon.ChevronLeft,
            null,
            KitButtonKind.Quiet,
            KitSize.Sm,
            () => { }
        );
        kit.Text(pager, "第 1–5 局，共 23 局", KitTextRole.Caption);
        kit.IconLabelButton(
            pager,
            "",
            null,
            KitIcon.ChevronRight,
            KitButtonKind.Quiet,
            KitSize.Sm,
            () => { }
        );
        DesktopKit.Rect(pager, "Gap").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        kit.Button(pager, "最新", KitButtonKind.Quiet, KitSize.Sm, () => { });
        var actions = Row(rail, "Actions", InGameSizes.Space8, TextAnchor.MiddleLeft);
        kit.Button(actions, "删除", KitButtonKind.Danger, KitSize.Md, () => { });
        DesktopKit.Rect(actions, "Gap").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        kit.Button(actions, "收藏", KitButtonKind.Secondary, KitSize.Md, () => { });
        kit.Button(actions, "回放", KitButtonKind.Primary, KitSize.Md, () => { });
    }

    // A run row as History will draw it: the kit's row, with the hero, the rank badge (a
    // ranked run) and one mark (a trophy tier, or a state mark) arriving after it.
    private void RunRow(
        Transform list,
        int index,
        string name,
        string meta,
        string? rank,
        int trophy,
        KitMark? mark,
        string result,
        bool key
    )
    {
        var row = _kit!.RunRow(
            list,
            RowHeight,
            name,
            meta,
            result,
            key ? InGameTheme.Accent : InGameTheme.Ink,
            () => SelectRow(index)
        );
        _rows.Add(row.Button);
        _portraitTargets.Add((row.Portrait, index % _heroes.Length));
        if (rank != null)
            _rankTargets.Add((row.Rank, rank));
        if (trophy >= 0)
            _trophyTargets.Add((row.Mark, trophy));
        else if (mark is { } state)
            DesktopKit.Show(row.Mark, DesktopKit.Mark(state));
    }

    // A row that ends in a quiet sm button (and, with left, starts in a quiet arrow): the
    // label and the arrow's ink sit on the row's edges, with the other right-aligned text of the rail.
    private static void Flush(RectTransform row, bool left)
    {
        var inset = -(int)DesktopKit.LabelInset(KitSize.Sm);
        row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(
            left ? -(int)DesktopKit.ArrowInset(KitSize.Sm) : 0,
            inset,
            0,
            0
        );
    }

    private void ButtonRow(Transform parent, string label, string text, KitButtonKind kind)
    {
        var kit = _kit!;
        var row = Row(parent, label, 0, TextAnchor.MiddleLeft);
        kit.Text(Cell(row, LabelColumn), label, KitTextRole.Caption);
        kit.Button(Cell(row, StateColumn), text, kind, KitSize.Md, () => { });
        kit.Button(Cell(row, StateColumn), text, kind, KitSize.Md, () => { }).Preview =
            KitPreviewState.Hover;
        kit.Button(Cell(row, StateColumn), text, kind, KitSize.Md, () => { }).Preview =
            KitPreviewState.Pressed;
        kit.Button(Cell(row, StateColumn), text, kind, KitSize.Md, () => { }).Dimmed = true;
        kit.Button(Cell(row, StateColumn), text, kind, KitSize.Md, () => { }).interactable = false;
        kit.Button(Cell(row, StateColumn), text, kind, KitSize.Sm, () => { });
    }

    private void Chip(Transform parent, string label, Color? ink, bool on)
    {
        KitButton? chip = null;
        chip = _kit!.Chip(parent, label, () => chip!.On = !chip.On);
        chip.InkOverride = ink;
        chip.On = on;
    }

    private void SelectRow(int index)
    {
        _selectedRow = index;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].On = i == _selectedRow;
        DesktopKit.Dividers(_rows);
    }

    private void SelectDay(int index)
    {
        _selectedDay = index;
        for (var i = 0; i < _days.Count; i++)
            _days[i].On = i == _selectedDay;
    }

    private void SelectSegment(int index)
    {
        for (var i = 0; i < _segments.Length; i++)
            _segments[i].On = i == index;
    }

    // ---- native art ----

    private void LoadArt()
    {
        if (_heroLoad == null)
        {
            var heroes = ((EHero[])Enum.GetValues(typeof(EHero)))
                .Where(HeroPortraitSpriteProvider.IsRenderableHero)
                .Take(_heroes.Length)
                .ToArray();
            _heroFocus = heroes.Select(HeroPortraitFocus.For).ToArray();
            _heroLoad = heroes
                .Select(HeroPortraitSpriteProvider.LoadDefaultPortraitAsync)
                .ToArray();
        }
        for (var i = 0; i < _heroLoad.Length; i++)
        {
            if (_heroes[i] != null || !_heroLoad[i].IsCompleted)
                continue;
            if (
                _heroLoad[i].Status == TaskStatus.RanToCompletion
                && _heroLoad[i].Result?.Sprite is { } sprite
            )
            {
                _heroes[i] = sprite;
                foreach (var (target, hero) in _portraitTargets)
                    if (hero == i)
                        DesktopKit.ShowPortrait(target, sprite, _heroFocus?[i]);
            }
        }
        if (_trophyLoad == null && _trophies.Any(sprite => sprite == null))
        {
            if (Services.TryGet<AssetLoader>(out var loader) && loader != null)
                _trophyLoad = Task.WhenAll(
                    TrophyAddresses.Select(address =>
                        NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
                    )
                );
        }
        else if (_trophyLoad?.IsCompleted == true)
        {
            if (_trophyLoad.Status == TaskStatus.RanToCompletion)
                for (var i = 0; i < _trophies.Length; i++)
                    _trophies[i] = _trophyLoad.Result[i];
            else
                _ = _trophyLoad.Exception;
            _trophyLoad = null;
            foreach (var (target, tier) in _trophyTargets)
                DesktopKit.Show(target, _trophies[tier]);
        }
    }

    private static string Diagnostics()
    {
        var arts = (DesktopKitArt[])Enum.GetValues(typeof(DesktopKitArt));
        var missing = arts.Where(art => DesktopNativeSkin.KitArt(art) == null)
            .Select(art =>
                $"{DesktopNativeSkin.KitArtName(art)}：{DesktopNativeSkin.KitArtProblem(art) ?? "加载中"}"
            )
            .ToArray();
        var plate = DesktopNativeSkin.KitArt(DesktopKitArt.Plate);
        var slice =
            plate != null
                ? $"\n按钮底 {plate.rect.width:0}×{plate.rect.height:0}，切片 {plate.border.x:0}/{plate.border.y:0}/{plate.border.z:0}/{plate.border.w:0}"
                : "";
        return $"原生贴图 {arts.Length - missing.Length}/{arts.Length} 已加载{slice}\n画布 1600×1000，match 0.5"
            + string.Concat(missing.Select(line => "\n" + line));
    }

    // ---- layout helpers ----

    private static RectTransform Column(
        Transform parent,
        string name,
        float spacing,
        int horizontal = 0,
        int vertical = 0
    )
    {
        var rect = DesktopKit.Rect(parent, name);
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(horizontal, horizontal, vertical, vertical);
        group.spacing = spacing;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return rect;
    }

    private static RectTransform Row(Transform parent, string name, float spacing, TextAnchor align)
    {
        var rect = DesktopKit.Rect(parent, name);
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.childAlignment = align;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        return rect;
    }

    // A fixed-width cell that keeps its control at the control's own width, left-aligned.
    private static RectTransform Cell(Transform row, float width)
    {
        var cell = Row(row, "Cell", 0, TextAnchor.MiddleLeft);
        DesktopKit.Size(cell, width, InGameSizes.ControlMd);
        return cell;
    }

    public void Dispose()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
    }
}
