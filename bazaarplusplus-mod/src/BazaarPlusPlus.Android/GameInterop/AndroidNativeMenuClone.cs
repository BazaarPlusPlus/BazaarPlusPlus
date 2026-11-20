using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

// Clone under an inactive holder so the game behaviours cannot wake before they are stripped.
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
