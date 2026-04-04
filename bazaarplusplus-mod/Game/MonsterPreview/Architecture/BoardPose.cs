#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class BoardPose
{
    public Vector3 Position { get; set; } = Vector3.zero;

    public Quaternion Rotation { get; set; } = Quaternion.identity;
}
