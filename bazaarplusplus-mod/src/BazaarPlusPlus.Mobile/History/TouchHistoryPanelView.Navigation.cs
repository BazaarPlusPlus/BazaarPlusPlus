using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

internal sealed partial class TouchHistoryPanelView
{
    private GameObject _archiveGroup = null!,
        _detailGroup = null!;
    private GameObject _titleObject = null!,
        _supportObject = null!;
    private HistoryButton _filter = null!,
        _back = null!,
        _opponentToggle = null!,
        _playerToggle = null!;
    private TextMeshProUGUI _supporterCredit = null!,
        _supporterAction = null!,
        _archiveEmpty = null!;
    private bool _snapshotsAvailable;
    private bool _restoreArchiveScroll;

    public bool HasDetailBackStep => _l.Compact && _navigation.DetailOpen;

    public void BackToArchive()
    {
        _navigation.Back();
        ApplyNavigation();
    }

    private void SelectSection(HistorySectionMode section)
    {
        _navigation.Back();
        _actions.Section(section);
        ApplyNavigation();
    }

    private void BuildSupporter()
    {
        var link = HeaderButton(
            "support",
            _l.Sponsor,
            () => Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(L.CurrentLanguageCode))
        );
        _supportObject = link.GameObject;
        link.Set(string.Empty);
        link.Label.gameObject.SetActive(false);
        _supporterCredit = TouchHistoryUi.Fraction(
            link.Button.transform,
            _font,
            .04f,
            0,
            .67f,
            1,
            _l.BodyFont
        );
        _supporterAction = TouchHistoryUi.Fraction(
            link.Button.transform,
            _font,
            .74f,
            0,
            .22f,
            1,
            _l.BodyFont,
            TextAlignmentOptions.Center,
            TouchHistoryUi.Accent
        );
        RefreshSupporter();
    }

    private void RefreshSupporter()
    {
        var credit = BPPSupporterAttribution.FormatRichLine(
            _headerSupporters,
            L.CurrentLanguageCode
        );
        _supporterCredit.text = string.IsNullOrEmpty(credit)
            ? TouchHistoryUi.T("Community support", "玩家社区支持")
            : credit;
        _supporterAction.text =
            BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode) + " ›";
        MobilePlatform.Port.CoverText(_supporterCredit);
    }

    private void BuildNavigation(GameObject archiveFill, GameObject tray)
    {
        _back = HeaderButton("back_to_records", _l.Back, BackToArchive);
        _back.Set(TouchHistoryUi.T("‹ Records", "‹ 记录"));
        _opponentToggle = HeaderButton(
                "show_opponent",
                _l.OpponentToggle,
                () =>
                {
                    _navigation.PlayerBoard = false;
                    ApplyNavigation();
                }
            )
            .As(HistoryButtonStyle.Tab);
        _playerToggle = HeaderButton(
                "show_player",
                _l.PlayerToggle,
                () =>
                {
                    _navigation.PlayerBoard = true;
                    ApplyNavigation();
                }
            )
            .As(HistoryButtonStyle.Tab);
        _archiveEmpty = TouchHistoryUi.Label(
            Surface,
            _font,
            "ArchiveEmpty",
            _l.ArchiveList,
            _l.BodyFont,
            TextAlignmentOptions.Center,
            TouchHistoryUi.Muted,
            wrap: true
        );
        _archiveGroup = Group(
            "ArchiveGroup",
            archiveFill,
            tray,
            _runsTab.GameObject,
            _ghostTab.GameObject,
            _favoritesTab.GameObject,
            _filter.GameObject,
            _archive.Scroll.gameObject,
            _pageLabel.gameObject,
            _newer.GameObject,
            _older.GameObject,
            _latest.GameObject,
            _archiveEmpty.gameObject
        );
        _detailGroup = Group(
            "DetailGroup",
            UpperBoard.gameObject,
            LowerBoard.gameObject,
            _upperTitle.gameObject,
            _lowerTitleObject,
            _upperStatusBand,
            _lowerStatusBand,
            _emptyDetail.gameObject,
            _delete.GameObject,
            _favorite.GameObject,
            _skills.GameObject,
            _replay.GameObject,
            _rail.Scroll.gameObject,
            _railNewer.GameObject,
            _railOlder.GameObject,
            _back.GameObject,
            _opponentToggle.GameObject,
            _playerToggle.GameObject
        );
    }

    private GameObject Group(string name, params GameObject[] children)
    {
        var group = UiKit.Rect(
            name,
            Surface,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        foreach (var child in children)
            child.transform.SetParent(group, false);
        return group.gameObject;
    }

    private void KeepScroll()
    {
        if (_archive == null || _boundSection is not { } section)
            return;
        _navigation.ArchiveScrollDp[(int)section] =
            Mathf.Max(0, _archive.Scroll.content.anchoredPosition.y) / _l.Metrics.UnitsPerDp;
        var position = _rail.Scroll.content.anchoredPosition;
        _navigation.BattleScrollSlots =
            Mathf.Max(0, _l.Compact ? -position.x : position.y)
            / (_l.Compact ? _l.ChipWidth : _l.ChipHeight);
    }

    private void ApplyNavigation()
    {
        if (_model is not { } model || _archiveGroup == null)
            return;
        var runs = model.SectionMode == HistorySectionMode.Runs;
        var rows = runs ? model.Runs.Count : model.VisibleBattles.Count;
        var detail = !_l.Compact || _navigation.DetailOpen;
        TouchHistoryUi.SetActive(_titleObject, !_l.Compact || !detail);
        TouchHistoryUi.SetActive(_supportObject, !_l.Compact || !detail);
        TouchHistoryUi.SetActive(_more.GameObject, !_l.Compact || !detail);
        var boards = detail && !_empty && _snapshotsAvailable;
        var upper = boards && (!_l.Compact || !model.ShowsBothBoards || !_navigation.PlayerBoard);
        var lower = boards && model.ShowsBothBoards && (!_l.Compact || _navigation.PlayerBoard);
        TouchHistoryUi.SetActive(_archiveGroup, !_l.Compact || !detail);
        TouchHistoryUi.SetActive(_detailGroup, detail);
        TouchHistoryUi.SetActive(
            _filter.GameObject,
            model.SectionMode != HistorySectionMode.Favorites
        );
        TouchHistoryUi.SetActive(_pageLabel.gameObject, rows > 0);
        TouchHistoryUi.SetActive(_newer.GameObject, rows > 0);
        TouchHistoryUi.SetActive(_older.GameObject, rows > 0);
        TouchHistoryUi.SetActive(_latest.GameObject, rows > 0);
        TouchHistoryUi.SetActive(_back.GameObject, _l.Compact);
        TouchHistoryUi.SetActive(
            _opponentToggle.GameObject,
            _l.Compact && model.ShowsBothBoards && !_empty
        );
        TouchHistoryUi.SetActive(
            _playerToggle.GameObject,
            _l.Compact && model.ShowsBothBoards && !_empty
        );
        _opponentToggle.Set(HistoryPanelText.BoardOpponent(), !_navigation.PlayerBoard);
        _playerToggle.Set(HistoryPanelText.BoardYou(), _navigation.PlayerBoard);
        TouchHistoryUi.SetActive(UpperBoard.gameObject, upper);
        TouchHistoryUi.SetActive(_lowerBoardObject, lower);
        TouchHistoryUi.SetActive(_upperTitle.gameObject, upper);
        TouchHistoryUi.SetActive(_lowerTitleObject, lower);
        TouchHistoryUi.SetActive(_upperStatusBand, upper && _upperMessage.Length > 0);
        TouchHistoryUi.SetActive(_lowerStatusBand, lower && _lowerMessage.Length > 0);
        TouchHistoryUi.SetActive(_skills.GameObject, boards);
        TouchHistoryUi.SetActive(_rail.Scroll.gameObject, runs && !_empty);
        TouchHistoryUi.SetActive(_railNewer.GameObject, runs && !_empty && _railPaged == true);
        TouchHistoryUi.SetActive(_railOlder.GameObject, runs && !_empty && _railPaged == true);
        if (!_empty && !_snapshotsAvailable)
            _emptyDetail.text = _upperMessage;
        TouchHistoryUi.SetActive(_emptyDetail.gameObject, !boards && _emptyDetail.text.Length > 0);
        _archiveEmpty.text = _emptyDetail.text;
        TouchHistoryUi.SetActive(_archiveEmpty.gameObject, _l.Compact && rows == 0);
    }
}
