#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BazaarPlusPlus.Game.ItemBoard;

internal sealed class ItemBoardTemplateSetRequest
{
    public IReadOnlyList<ItemBoardItemSpec> Items { get; set; } = new List<ItemBoardItemSpec>();

    public Vector2? AnchoredPosition { get; set; }

    public float Scale { get; set; } = 1f;

    public string? SponsorText { get; set; }

    public string? SponsorName { get; set; }

    public int SponsorTier { get; set; }

    public int CandidateIndex { get; set; }

    public int CandidateCount { get; set; }

    public bool IsAlertState { get; set; }

    public float ShowTime { get; set; } = 0.18f;

    public ItemBoardTemplateSetRequest Clone()
    {
        return new ItemBoardTemplateSetRequest
        {
            Items =
                Items?.Select(item => item?.Clone() ?? new ItemBoardItemSpec()).ToList()
                ?? new List<ItemBoardItemSpec>(),
            AnchoredPosition = AnchoredPosition,
            Scale = Scale,
            SponsorText = SponsorText,
            SponsorName = SponsorName,
            SponsorTier = SponsorTier,
            CandidateIndex = CandidateIndex,
            CandidateCount = CandidateCount,
            IsAlertState = IsAlertState,
            ShowTime = ShowTime,
        };
    }
}
