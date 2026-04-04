#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class FixedAnchorStrategy : IBoardAnchorStrategy
{
    public Vector3 Position { get; set; } = Vector3.zero;

    public Quaternion Rotation { get; set; } = Quaternion.identity;

    public FixedAnchorStrategy(BoardPose pose)
    {
        if (pose != null)
        {
            Position = pose.Position;
            Rotation = pose.Rotation;
        }
    }

    public FixedAnchorStrategy() { }

    public void SetPose(BoardPose pose)
    {
        if (pose == null)
            return;

        Position = pose.Position;
        Rotation = pose.Rotation;
    }

    public bool TryResolve(out BoardPose pose)
    {
        pose = new BoardPose { Position = Position, Rotation = Rotation };
        return true;
    }
}
