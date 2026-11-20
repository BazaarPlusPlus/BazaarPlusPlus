using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile;
using Il2CppTheBazaar;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

// The live board shares this popup, so restore its original canvas sorting order when the rented
// skills close.
internal sealed class AndroidNativeSkillOverlay(int sortingOrder) : INativeSkillOverlay
{
    private GameObject? _proxies;
    private Il2Cpp.MobileSkillOverlayController? _overlay;
    private bool _shown;
    private (Canvas Canvas, int Order, bool Override)? _raised;

    public bool IsOpen => _proxies != null;

    public bool Open(IReadOnlyList<CardRentalSubject> skills, string source)
    {
        Close("reopened");
        var popups = AndroidNativeHud.Popups;
        if (popups == null || skills.Count == 0)
            return false;
        var root = new GameObject("BPP.SkillProxies");
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);
        var proxies = new Il2CppSystem.Collections.Generic.List<SkillProxyRenderer>();
        foreach (var skill in skills)
        {
            if (AndroidCardTemplateText.PreviewCard(skill) is not { } card)
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
        popups.ShowMobileSkillOverlay(
            proxies.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<SkillProxyRenderer>>(),
            false
        );
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_skills.requested"),
            ("source", source),
            ("skills", proxies.Count)
        );
        return true;
    }

    // The overlay loads on first use, so it can be absent for several frames after an open request.
    public void Tick()
    {
        if (_proxies == null)
            return;
        if (_overlay == null)
            _overlay =
                UnityEngine.Object.FindFirstObjectByType<Il2Cpp.MobileSkillOverlayController>();
        var showing = _overlay != null && _overlay.gameObject.activeInHierarchy;
        if (showing && !_shown)
        {
            _shown = true;
            Raise(_overlay!);
        }
        else if (!showing && _shown)
            Close("closed");
    }

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
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_skills.closed"),
            ("reason", reason),
            ("shown", _shown)
        );
        return true;
    }

    private void Raise(Il2Cpp.MobileSkillOverlayController overlay)
    {
        var canvas = overlay.GetComponent<Canvas>() ?? overlay.gameObject.AddComponent<Canvas>();
        var root = canvas.rootCanvas;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_skills.shown"),
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
