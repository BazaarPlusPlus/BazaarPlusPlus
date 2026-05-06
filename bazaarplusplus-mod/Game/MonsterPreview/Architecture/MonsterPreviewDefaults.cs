#pragma warning disable CS0436
using BazaarPlusPlus.Game.PreviewSurface;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal static class MonsterPreviewDefaults
{
    public static readonly BoardPose DefaultAnchorPose = new BoardPose
    {
        Position = new Vector3(0f, 6f, -1.5f),
        Rotation = Quaternion.identity,
    };

    public static PreviewBoardPresentation CreateDebugPresentation()
    {
        return CreateShowcasePresentation();
    }

    public static PreviewBoardPresentation CreateShowcasePresentation()
    {
        return new PreviewBoardPresentation
        {
            Visible = true,
            LocalOffset = new Vector3(0f, 0.1f, 0f),
            CardSpacing = new Vector3(1.1f, 0f, 0f),
            CardScale = Vector3.one * 0.9f,
            BoardSize = new Vector2(14.5f, 3f),
            SkillBoardWidth = 1.3f,
            BoardThickness = 0.01f,
            BorderThickness = 0.02f,
            BorderHeight = 0.04f,
        };
    }
}
