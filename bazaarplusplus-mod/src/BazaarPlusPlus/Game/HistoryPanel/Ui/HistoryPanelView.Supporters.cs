#nullable enable
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private TextMeshProUGUI _supporterPrefix = null!,
        _supporterAction = null!;
    private readonly TextMeshProUGUI[] _supporterNames = new TextMeshProUGUI[4];

    private void BuildSupporterHeader()
    {
        var link = Button(
            _layout!,
            "",
            HistoryPanelLayout.SupportButton,
            () => Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(SupportLanguage()))
        );
        ButtonLabel(link).gameObject.SetActive(false);
        _supporterPrefix = Text(link.transform, "", .025f, 0, .12f, 1, 14, Muted);
        for (var i = 0; i < _supporterNames.Length; i++)
        {
            var name = Text(link.transform, "", .15f + i * .16f, 0, .15f, 1, 15);
            name.richText = false;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            _supporterNames[i] = name;
        }
        _supporterAction = Text(
            link.transform,
            "",
            .81f,
            0,
            .165f,
            1,
            15,
            InGameTheme.Accent,
            true
        );
    }

    private static string SupportLanguage() => PlayerPreferences.Data.LanguageCode ?? string.Empty;

    private void BindSupporters(IReadOnlyList<BPPSupporterSample> samples)
    {
        var language = SupportLanguage();
        _supporterPrefix.text = HistoryPanelText.Thanks();
        _supporterAction.text = BPPSupporterAttributionText.FormatSponsorAction(language) + " ›";
        var names = samples.Where(sample => sample.HasValue).Take(4).ToArray();
        for (var i = 0; i < _supporterNames.Length; i++)
        {
            var name = _supporterNames[i];
            name.text =
                i < names.Length ? names[i].Name
                : i == 0 ? HistoryPanelText.Community()
                : "";
            name.color =
                i < names.Length
                && ColorUtility.TryParseHtmlString(
                    "#" + BPPSupporterAttribution.TierHex(names[i].Tier),
                    out var color
                )
                    ? color
                    : Muted;
        }
    }

    internal bool HasOpenModal =>
        _filterRoot?.activeSelf == true || _moreVisible || _noteRoot?.activeSelf == true;

    internal void DismissModal()
    {
        if (_noteRoot?.activeSelf == true)
            CloseNoteDialog();
        else if (_filterRoot?.activeSelf == true)
            _filterRoot.SetActive(false);
        else if (_moreVisible)
            SetMoreVisible(false);
    }
}
