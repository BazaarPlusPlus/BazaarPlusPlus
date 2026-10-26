using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Clones a native menu control for a BPP entry: the copy keeps Unity's and third-party
/// rendering and layout (images, text, shadows, layout groups) and loses every game behaviour
/// (scene selection, pooling, button controllers, sound, localization) and its selectable, so
/// nothing of the game's own wiring reacts to it. The copy is made under an inactive holder, so
/// none of those behaviours wakes before it goes. Main thread only.
/// </summary>
internal static class AndroidNativeMenuClone
{
    private static readonly HashSet<string> GameAssemblies = new(StringComparer.Ordinal)
    {
        "Assembly-CSharp",
        "TheBazaarRuntime",
    };

    public static GameObject Create(GameObject donor, string name)
    {
        var holder = new GameObject("BPP.MenuCloneHolder");
        holder.SetActive(false);
        try
        {
            var clone = UnityEngine.Object.Instantiate(donor, holder.transform, false);
            clone.name = name;
            Strip(clone);
            clone.transform.SetParent(donor.transform.parent, false);
            return clone;
        }
        finally
        {
            UnityEngine.Object.Destroy(holder);
        }
    }

    /// <summary>A Unity button on <paramref name="target"/> that tints <paramref name="graphic"/>.</summary>
    public static Button AddButton(GameObject target, Graphic? graphic, Action tap)
    {
        var button = target.AddComponent<Button>();
        button.targetGraphic = graphic;
        button.onClick.AddListener((UnityEngine.Events.UnityAction)tap);
        return button;
    }

    public static void SetLabel(GameObject root, string text)
    {
        var label = root.GetComponentInChildren<TMP_Text>(false);
        if (label != null)
            label.text = text;
    }

    // Selectables go last: the game's button controllers and tweeners depend on them.
    private static void Strip(GameObject clone)
    {
        var selectables = new List<Selectable>();
        foreach (var component in clone.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;
            if (component.TryCast<Selectable>() is { } selectable)
            {
                selectables.Add(selectable);
                continue;
            }
            var assembly = component.GetIl2CppType().Assembly.GetName().Name;
            if (GameAssemblies.Contains(assembly))
                UnityEngine.Object.DestroyImmediate(component);
        }
        foreach (var selectable in selectables)
            UnityEngine.Object.DestroyImmediate(selectable);
    }
}
