#nullable enable
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile;
using TheBazaar;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The game's own mobile skill overlay (<c>MobileSkillOverlayController</c>, opened through
/// <c>PopupManager.ShowMobileSkillOverlay</c>) over skills that are not on a live board, as the
/// end-of-run summary opens it. Each skill rides a bare <see cref="SkillProxyRenderer"/> that
/// carries only its card and tier: the overlay reads nothing else, loading the icon from the
/// card's template art and the frame from its own tier frames. The proxies live under an inactive
/// root and are destroyed when the overlay closes. While open, the overlay's canvas sorts at
/// <paramref name="sortingOrder"/> so it stands over the BPP panel that opened it; its own order
/// comes back on close, since the board opens the same overlay. Main thread only.
/// </summary>
internal sealed class TouchNativeSkillOverlay(int sortingOrder) : INativeSkillOverlay
{
    private GameObject? _proxies;
    private global::MobileSkillOverlayController? _overlay;
    private bool _shown;
    private (Canvas Canvas, int Order, bool Override)? _raised;

    /// <summary>Requested or showing; the overlay owns input until it closes.</summary>
    public bool IsOpen => _proxies != null;

    /// <summary>False when the game has no popup manager or none of the skills builds a card.</summary>
    public bool Open(IReadOnlyList<CardRentalSubject> skills, string source)
    {
        Close("reopened");
        var popups = TouchNativeHud.Popups;
        if (popups == null || skills.Count == 0)
            return false;
        var root = new GameObject("BPP.SkillProxies");
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);
        var proxies = new List<SkillProxyRenderer>();
        foreach (var skill in skills)
        {
            if (TouchCardText.PreviewCard(skill) is not { } card)
                continue;
            var proxy = new GameObject("Skill").AddComponent<SkillProxyRenderer>();
            proxy.transform.SetParent(root.transform, false);
            proxy.Card = card;
            proxy.Tier = card.Tier;
            proxies.Add(proxy);
        }
        if (proxies.Count == 0)
        {
            UnityEngine.Object.Destroy(root);
            return false;
        }
        _proxies = root;
        _shown = false;
        popups.ShowMobileSkillOverlay(proxies, false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.native_skills.requested"),
            ("source", source),
            ("skills", proxies.Count)
        );
        return true;
    }

    /// <summary>
    /// Every frame while the opener is visible: notices the overlay showing and closing. The
    /// overlay loads on first use, so a request stays open until it shows and closes, or until
    /// the opener closes it.
    /// </summary>
    public void Tick()
    {
        if (_proxies == null)
            return;
        if (_overlay == null)
            _overlay =
                UnityEngine.Object.FindFirstObjectByType<global::MobileSkillOverlayController>();
        var showing = _overlay != null && _overlay.gameObject.activeInHierarchy;
        if (showing && !_shown)
        {
            _shown = true;
            Raise(_overlay!);
        }
        else if (!showing && _shown)
            Close("closed");
    }

    /// <summary>False when it was not open.</summary>
    public bool Close(string reason)
    {
        if (_proxies == null)
            return false;
        if (_overlay != null && _overlay.gameObject.activeInHierarchy)
            _overlay.Hide();
        if (_raised is { } raised && raised.Canvas != null)
        {
            raised.Canvas.sortingOrder = raised.Order;
            raised.Canvas.overrideSorting = raised.Override;
        }
        _raised = null;
        UnityEngine.Object.Destroy(_proxies);
        _proxies = null;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.native_skills.closed"),
            ("reason", reason),
            ("shown", _shown)
        );
        return true;
    }

    private void Raise(global::MobileSkillOverlayController overlay)
    {
        var canvas = overlay.GetComponent<Canvas>() ?? overlay.gameObject.AddComponent<Canvas>();
        var root = canvas.rootCanvas;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.native_skills.shown"),
            ("root_mode", root.renderMode),
            ("root_order", root.sortingOrder),
            ("own_root", canvas.isRootCanvas)
        );
        _raised = (canvas, canvas.sortingOrder, canvas.overrideSorting);
        if (!canvas.isRootCanvas)
            canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        if (overlay.GetComponent<GraphicRaycaster>() == null)
            overlay.gameObject.AddComponent<GraphicRaycaster>();
    }
}
