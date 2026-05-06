#nullable enable
using BazaarGameShared.Domain.Players;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.Game.ItemBoard;

internal sealed class ItemBoardRenderInput
{
    public TMonster? Monster { get; set; }

    public CarpetAssetDataSO? Carpet { get; set; }

    public Vector2? AnchoredPosition { get; set; }

    public float Scale { get; set; } = 1f;

    public float ShowTime { get; set; } = 0.18f;
}
