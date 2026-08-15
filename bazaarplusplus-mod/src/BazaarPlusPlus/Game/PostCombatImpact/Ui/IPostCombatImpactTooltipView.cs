#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal enum CombatImpactPerspective
{
    Caused,
    Received,
}

internal interface IPostCombatImpactTooltipView : IDisposable
{
    string Header { get; }

    bool IsReadyToReveal { get; }

    bool IsContentActive { get; }

    bool CanSwitchPerspective { get; }

    void PrepareNativePrimary(CardTooltipController primary);

    void CancelPreparedNativePrimary(CardTooltipController primary);

    void PrepareNativeAuxiliary(AuxiliaryTooltipController auxiliary);

    void CancelPreparedNativeAuxiliary(AuxiliaryTooltipController auxiliary);

    bool Show(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        string entityName,
        bool isSkill,
        CombatImpactSource? source,
        CombatImpactReceived? received,
        CombatImpactPerspective perspective
    );

    bool SetPerspective(CombatImpactPerspective perspective, UnityEngine.Transform anchor);

    bool Position(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        UnityEngine.Transform anchor
    );

    void Reveal();

    void Hide();

    bool OnNativeTooltipChanging(CardTooltipController controller);

    bool OnNativeAuxiliaryTooltipShowing(AuxiliaryTooltipController controller);

    bool OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller);

    bool TryConcealVisibleEmptyNativeAuxiliary(AuxiliaryTooltipController controller);
}
