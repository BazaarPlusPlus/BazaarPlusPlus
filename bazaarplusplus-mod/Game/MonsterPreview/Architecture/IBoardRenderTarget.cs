#pragma warning disable CS0436
using System;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IBoardRenderTarget : IDisposable
{
    bool IsAlive { get; }

    void Render(BoardRenderModel renderModel);

    void SetVisible(bool visible);
}
