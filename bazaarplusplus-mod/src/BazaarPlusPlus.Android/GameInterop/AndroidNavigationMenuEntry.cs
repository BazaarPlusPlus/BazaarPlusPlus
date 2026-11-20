using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

// The native menu fades only its pooled tiles, so the BPP tile must track that fade separately.
internal sealed class AndroidNavigationMenuEntry(
    string name,
    Func<string> label,
    Sprite icon,
    Func<bool> offered,
    Action open
)
{
    private const float FadeSeconds = 0.25f;

    private NavigationMenuController? _menu;
    private float _nextLookup;
    private GameObject? _tile;
    private CanvasGroup? _fade;
    private string? _label;

    public void Tick()
    {
        if (_menu == null)
        {
            if (Time.unscaledTime < _nextLookup)
                return;
            _nextLookup = Time.unscaledTime + 1f;
            // A scene-wide menu search can stall a frame during a run, where the menu cannot exist.
            if (InRunScene())
                return;
            _menu = UnityEngine.Object.FindFirstObjectByType<NavigationMenuController>();
            if (_menu == null)
                return;
        }
        var pooler = _menu._pooler;
        if (pooler == null || !pooler.gameObject.activeInHierarchy)
            return;
        if (_tile == null && !TryMount(pooler))
            return;
        var show = offered();
        if (_tile!.activeSelf != show)
            _tile.SetActive(show);
        if (!show)
            return;
        var content = _tile.transform.parent;
        if (_tile.transform.GetSiblingIndex() != content.childCount - 1)
            _tile.transform.SetAsLastSibling();
        var text = label();
        if (!string.Equals(text, _label, StringComparison.Ordinal))
        {
            _label = text;
            AndroidNativeMenuClone.SetLabel(_tile, text);
        }
        if (_fade != null)
            _fade.alpha = Mathf.MoveTowards(
                _fade.alpha,
                _menu._menuOpen ? 1f : 0f,
                Time.unscaledDeltaTime / FadeSeconds
            );
    }

    private static bool InRunScene()
    {
        try
        {
            return Il2Cpp.SceneLoader.ActiveScene == Il2CppTheBazaar.SceneID.GameScene;
        }
        catch
        {
            return false;
        }
    }

    private bool TryMount(Il2CppTheBazaar.UI.PooledScrollRectBase pooler)
    {
        SceneSelectOptionContainer? donor = null;
        foreach (var element in pooler._activeElements)
        {
            donor = element?.GetComponent<SceneSelectOptionContainer>();
            if (donor != null)
                break;
        }
        if (donor == null)
            return false;
        var tile = AndroidNativeMenuClone.Create(donor.gameObject, "BPP." + name);
        Image? background = null;
        foreach (var image in tile.GetComponentsInChildren<Image>(true))
        {
            switch (image.gameObject.name)
            {
                case "Btn_Background":
                    background = image;
                    break;
                case "Btn_Highlight":
                    // The highlight marks the active scene; no BPP tile is one.
                    image.gameObject.SetActive(false);
                    break;
                case "Btn_Icon":
                    image.sprite = icon;
                    image.preserveAspect = true;
                    break;
            }
        }
        _fade = tile.GetComponentInChildren<CanvasGroup>(true);
        if (_fade != null)
        {
            // A donor cloned mid-tween carries the game's show offsets.
            if (_fade.transform.TryCast<RectTransform>() is { } offset)
            {
                offset.localScale = Vector3.one;
                offset.anchoredPosition = Vector2.zero;
            }
            _fade.alpha = 0f;
        }
        AndroidNativeMenuClone.AddButton(tile, background, Tap);
        tile.transform.SetAsLastSibling();
        _tile = tile;
        _label = null;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_menu.tile_mounted"),
            ("menu", "navigation"),
            ("tile", name),
            ("donor", donor.name),
            ("index", tile.transform.GetSiblingIndex())
        );
        return true;
    }

    private void Tap()
    {
        if (_menu != null && _menu._menuOpen)
            _menu.OnMenuSelected();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_menu.tile_tapped"),
            ("menu", "navigation"),
            ("tile", name)
        );
        open();
    }
}
