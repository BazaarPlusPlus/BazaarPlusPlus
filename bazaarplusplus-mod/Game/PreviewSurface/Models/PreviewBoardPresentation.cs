using UnityEngine;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewBoardPresentation
{
    public bool Visible { get; set; }

    public bool DebugEnabled { get; set; }

    public bool ShowSkillBoard { get; set; } = true;

    public bool ShowBrandingBoard { get; set; } = true;

    public bool ShowMonsterInfoBoard { get; set; } = true;

    public bool ShowItemBoardFill { get; set; } = true;

    public Vector3 LocalOffset { get; set; } = Vector3.zero;

    public Vector3 CardScale { get; set; } = Vector3.one;

    public Vector3 CardSpacing { get; set; } = new Vector3(1.1f, 0f, 0f);

    public Vector2 BoardSize { get; set; } = new Vector2(8.25f, 2.75f);

    public float SkillBoardWidth { get; set; } = 2.4f;

    public float BoardThickness { get; set; } = 0.04f;

    public float BorderThickness { get; set; } = 0.08f;

    public float BorderHeight { get; set; } = 0.06f;
}
