using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// One BPP row in the game's in-run menu (the settings button's <c>FightMenuDialog</c>): a clone
/// of the Report Bug row (<see cref="AndroidNativeMenuClone"/>) placed right after Resume. The
/// column has a fixed height, so the rows' spacing shrinks to keep every row inside the frame,
/// and comes back while the row is hidden. Like the game's Return to Main Menu row, the row dims
/// and takes no tap while <paramref name="available"/> is false (a combat or a replay). A tap
/// closes the menu as Resume does, then runs <paramref name="open"/>. Main thread only.
/// </summary>
internal sealed class AndroidFightMenuEntry(
    string name,
    Func<string> label,
    Func<bool> offered,
    Func<bool> available,
    Action open
)
{
    private const float DimmedAlpha = 0.4f;

    private Il2Cpp.FightMenuDialog? _dialog;
    private float _nextLookup;
    private GameObject? _row;
    private Button? _button;
    private TMP_Text? _text;
    private VerticalLayoutGroup? _column;
    private float _nativeSpacing;
    private string? _label;

    public void Tick()
    {
        if (_dialog == null)
        {
            if (Time.unscaledTime < _nextLookup)
                return;
            _nextLookup = Time.unscaledTime + 1f;
            _dialog = UnityEngine.Object.FindFirstObjectByType<Il2Cpp.FightMenuDialog>();
            if (_dialog == null)
                return;
        }
        var panel = _dialog.fightMenuDialog;
        if (panel == null || !panel.activeInHierarchy)
            return;
        if (_row == null && !TryMount())
            return;
        var show = offered();
        if (_row!.activeSelf != show)
        {
            _row.SetActive(show);
            Fit();
        }
        if (!show)
            return;
        var text = label();
        if (!string.Equals(text, _label, StringComparison.Ordinal))
        {
            _label = text;
            if (_text != null)
                _text.text = text;
        }
        var interactable = available();
        if (_button!.interactable != interactable)
        {
            _button.interactable = interactable;
            if (_text != null)
                _text.alpha = interactable ? 1f : DimmedAlpha;
        }
    }

    private bool TryMount()
    {
        var donor = _dialog!.BugReportButton;
        var resume = _dialog.continueButton;
        if (donor == null || resume == null)
            return false;
        _column = donor.transform.parent.GetComponent<VerticalLayoutGroup>();
        if (_column == null)
            return false;
        _nativeSpacing = _column.spacing;
        var row = AndroidNativeMenuClone.Create(donor.gameObject, "BPP." + name);
        row.transform.SetSiblingIndex(resume.transform.GetSiblingIndex() + 1);
        _text = row.GetComponentInChildren<TMP_Text>(false);
        Graphic? center = null;
        foreach (var image in row.GetComponentsInChildren<Image>(true))
            if (image.gameObject.name == "Btn_Center")
                center = image;
        _button = AndroidNativeMenuClone.AddButton(row, center, Tap);
        _row = row;
        _label = null;
        Fit();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_menu.tile_mounted"),
            ("menu", "fight"),
            ("tile", name),
            ("donor", donor.name),
            ("index", row.transform.GetSiblingIndex()),
            ("spacing", _column.spacing)
        );
        return true;
    }

    // The native spacing while the native rows fit, else the spacing that keeps them in the frame.
    private void Fit()
    {
        if (_column == null)
            return;
        var rect = _column.transform.Cast<RectTransform>();
        var rows = 0;
        var heights = 0f;
        for (var i = 0; i < rect.childCount; i++)
        {
            var child = rect.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            rows++;
            heights += child.Cast<RectTransform>().rect.height;
        }
        if (rows < 2)
            return;
        var free = rect.rect.height - _column.padding.top - _column.padding.bottom - heights;
        _column.spacing = Math.Min(_nativeSpacing, free / (rows - 1));
    }

    private void Tap()
    {
        if (_dialog != null)
            _dialog.OnContinueClick();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_menu.tile_tapped"),
            ("menu", "fight"),
            ("tile", name)
        );
        open();
    }
}
