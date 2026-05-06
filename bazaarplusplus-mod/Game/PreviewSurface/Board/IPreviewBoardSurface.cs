using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.MonsterPreview;
using UnityEngine;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal interface IPreviewBoardSurface : IDisposable
{
    Transform RootTransform { get; }

    bool IsAlive { get; }

    void SetPresentation(PreviewBoardPresentation presentation);

    void SetDebugOptions(PreviewBoardDebugOptions debugOptions);

    void SetVisible(bool visible);

    void UpdateAnchor(Vector3 position, Quaternion rotation);

    Task RenderAsync(PreviewBoardModel model, CancellationToken cancellationToken = default);

    void Clear();
}
