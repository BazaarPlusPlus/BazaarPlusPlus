#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Host;

internal sealed class ComponentMount<T> : IBppMountable
    where T : Component
{
    private readonly Action<T, IBppServices>? _initialize;

    public ComponentMount(Action<T, IBppServices>? initialize = null)
    {
        _initialize = initialize;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var component = host.AddComponent<T>();
        _initialize?.Invoke(component, services);
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<T>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }
}
