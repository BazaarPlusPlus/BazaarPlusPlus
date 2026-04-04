#pragma warning disable CS0436
using System;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterLockShowcaseController
{
    public bool ShouldConsumeNextClickToClosePreview(
        bool isPreviewActive,
        bool closeOnNextClickArmed,
        bool isLeftClick,
        bool isRightClick
    )
    {
        if (!isPreviewActive || !closeOnNextClickArmed)
            return false;

        return isLeftClick || isRightClick;
    }

    public bool ShouldInterceptLockToggle(
        bool isPreviewActive,
        bool hasCurrentCard,
        bool isShowcaseCard,
        bool isMonsterCard
    )
    {
        if (!hasCurrentCard)
            return false;

        if (isShowcaseCard)
            return isPreviewActive;

        return isMonsterCard;
    }

    public bool ShouldShowForLock(Guid? lockedCardId, bool isShowcaseCard, bool isMonsterCard)
    {
        return lockedCardId.HasValue && !isShowcaseCard && isMonsterCard;
    }
}
