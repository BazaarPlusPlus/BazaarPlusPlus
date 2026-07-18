#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativePreviewSessionActions
{
    private readonly object _gate = new();
    private bool _disposed;
    private bool _shown;
    private bool _hovered;

    internal NativePreviewActionResult SetShown(
        bool scopeActive,
        bool show,
        Func<NativePreviewActionResult> apply
    )
    {
        lock (_gate)
        {
            if (_disposed || !scopeActive)
                return Released();
            if (_shown == show)
                return AlreadyApplied();

            var result = apply();
            if (result.Status == NativePreviewActionStatus.Applied)
                _shown = show;
            return result;
        }
    }

    internal NativePreviewActionResult HoverEnter(
        bool scopeActive,
        Func<NativePreviewActionResult> apply
    )
    {
        lock (_gate)
        {
            if (_disposed || !scopeActive)
                return Released();
            if (_hovered)
                return AlreadyApplied();

            var result = apply();
            if (result.Status == NativePreviewActionStatus.Applied)
                _hovered = true;
            return result;
        }
    }

    internal NativePreviewActionResult HoverExit(
        bool scopeActive,
        Func<NativePreviewActionResult> apply
    )
    {
        lock (_gate)
        {
            if (_disposed || !scopeActive)
                return Released();
            if (!_hovered)
                return AlreadyApplied();

            var result = apply();
            if (result.Status == NativePreviewActionStatus.Applied)
                _hovered = false;
            return result;
        }
    }

    internal bool TryDispose(out bool wasHovered)
    {
        lock (_gate)
        {
            wasHovered = _hovered;
            if (_disposed)
                return false;

            _disposed = true;
            _hovered = false;
            return true;
        }
    }

    private static NativePreviewActionResult Released() =>
        new(NativePreviewActionStatus.Released, null);

    private static NativePreviewActionResult AlreadyApplied() =>
        new(NativePreviewActionStatus.AlreadyApplied, null);
}
