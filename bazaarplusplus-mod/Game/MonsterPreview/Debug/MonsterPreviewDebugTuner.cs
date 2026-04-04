#pragma warning disable CS0436
using System;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewDebugTuner
{
    private const float MinBoardSize = 1f;
    private const float MinSpacing = 0.2f;
    private const float MinScale = 0.1f;
    private const float MinThickness = 0.01f;

    private readonly FixedAnchorStrategy _anchor;
    private readonly PreviewBoardPresentation _presentation;

    public MonsterPreviewDebugTuner(
        FixedAnchorStrategy anchor,
        PreviewBoardPresentation presentation
    )
    {
        _anchor = anchor;
        _presentation = presentation;
    }

    public void MoveAnchor(Vector3 delta)
    {
        _anchor.Position += delta;
    }

    public void RotateAnchorY(float degrees)
    {
        _anchor.Rotation = Quaternion.Euler(0f, degrees, 0f) * _anchor.Rotation;
    }

    public void ResetAnchor(BoardPose pose)
    {
        if (pose == null)
            return;

        _anchor.Position = pose.Position;
        _anchor.Rotation = pose.Rotation;
    }

    public void AdjustBoardWidth(float delta)
    {
        _presentation.BoardSize = new Vector2(
            Math.Max(MinBoardSize, _presentation.BoardSize.x + delta),
            _presentation.BoardSize.y
        );
    }

    public void AdjustBoardHeight(float delta)
    {
        _presentation.BoardSize = new Vector2(
            _presentation.BoardSize.x,
            Math.Max(MinBoardSize, _presentation.BoardSize.y + delta)
        );
    }

    public void AdjustSpacingX(float delta)
    {
        _presentation.CardSpacing = new Vector3(
            Math.Max(MinSpacing, _presentation.CardSpacing.x + delta),
            _presentation.CardSpacing.y,
            _presentation.CardSpacing.z
        );
    }

    public void AdjustCardScale(float delta)
    {
        _presentation.CardScale =
            Vector3.one * Math.Max(MinScale, _presentation.CardScale.x + delta);
    }

    public void AdjustBoardThickness(float delta)
    {
        _presentation.BoardThickness = Math.Max(MinThickness, _presentation.BoardThickness + delta);
    }

    public void AdjustBorderThickness(float delta)
    {
        _presentation.BorderThickness = Math.Max(
            MinThickness,
            _presentation.BorderThickness + delta
        );
    }

    public void AdjustBorderHeight(float delta)
    {
        _presentation.BorderHeight = Math.Max(MinThickness, _presentation.BorderHeight + delta);
    }
}
