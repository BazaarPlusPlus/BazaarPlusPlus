using BazaarPlusPlus.Core.Ui;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

// Height-scaled game canvases get too narrow for native layouts below the game's
// design aspect. Adjust their scale to preserve the layout width.
internal sealed class TouchScreenFillCanvases
{
    private readonly IScreenFillHost _host;

    private readonly Dictionary<int, (CanvasScaler Scaler, float Match)> _original = new();
    private readonly List<int> _destroyed = new();

    public TouchScreenFillCanvases(IScreenFillHost host) => _host = host;

    public void Apply(float aspect)
    {
        if (!ScreenFill.Fills(aspect))
        {
            // A popup can be hidden while the screen widens. Restore retained objects directly,
            // including inactive canvases, before stopping scene searches on a wide screen.
            foreach (var (scaler, match) in _original.Values)
                if (
                    Il2CppUi.IsAlive(scaler)
                    && Math.Abs(scaler.matchWidthOrHeight - match) > 0.001f
                )
                    scaler.matchWidthOrHeight = match;
            _original.Clear();
            return;
        }
        foreach (var (id, retained) in _original)
            if (!Il2CppUi.IsAlive(retained.Scaler))
                _destroyed.Add(id);
        foreach (var id in _destroyed)
            _original.Remove(id);
        _destroyed.Clear();
        foreach (var scaler in _host.CanvasScalers())
        {
            if (
                !Il2CppUi.IsAlive(scaler)
                || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
            )
                continue;
            var id = scaler.GetInstanceID();
            if (!_original.TryGetValue(id, out var retained) || !Il2CppUi.IsAlive(retained.Scaler))
            {
                var reference = scaler.referenceResolution;
                var original = scaler.matchWidthOrHeight;
                if (
                    !ScreenFill.TakesCanvas(
                        reference.x,
                        reference.y,
                        original,
                        scaler.gameObject.name
                    )
                )
                    continue;
                retained = (scaler, original);
                _original[id] = retained;
            }
            var match = ScreenFill.GameCanvasMatch(aspect, retained.Match);
            if (Math.Abs(scaler.matchWidthOrHeight - match) > 0.001f)
                scaler.matchWidthOrHeight = match;
        }
    }
}
