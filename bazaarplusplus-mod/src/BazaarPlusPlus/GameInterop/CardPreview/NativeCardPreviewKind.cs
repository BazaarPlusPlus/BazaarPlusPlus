#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal readonly struct NativeCardPreviewKind : IEquatable<NativeCardPreviewKind>
{
    public NativeCardPreviewKind(ECardType type, ECardSize size)
    {
        Type = type;
        Size = size;
    }

    public ECardType Type { get; }
    public ECardSize Size { get; }

    public static NativeCardPreviewKind ForSkill() => new(ECardType.Skill, ECardSize.Medium);

    public static NativeCardPreviewKind ForItem(ECardSize size) => new(ECardType.Item, size);

    public bool Equals(NativeCardPreviewKind other) => Type == other.Type && Size == other.Size;

    public override bool Equals(object? obj) => obj is NativeCardPreviewKind other && Equals(other);

    public override int GetHashCode() => ((int)Type * 397) ^ (int)Size;

    public override string ToString() => Type == ECardType.Skill ? "Skill" : $"Item-{Size}";
}
