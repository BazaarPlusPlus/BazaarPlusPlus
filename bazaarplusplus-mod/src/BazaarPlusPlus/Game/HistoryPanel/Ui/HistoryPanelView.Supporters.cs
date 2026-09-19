#nullable enable
using BazaarPlusPlus.Game.Supporters.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private BPPSupporterNativeAttributionRow? _supporterAttribution;

    private void BuildSupporterHeader()
    {
        var header = CreateRect("SupporterHeader", _layout!, .415f, .04f, .33f, .045f);
        _supporterAttribution = new BPPSupporterNativeAttributionRow(
            header,
            _font!,
            TextAnchor.MiddleRight
        );
        _supporterAttribution.SetSkin(_skin?[0], _skin?[1]);
    }
}
