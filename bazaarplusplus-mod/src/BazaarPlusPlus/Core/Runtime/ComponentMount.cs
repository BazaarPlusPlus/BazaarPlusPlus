#nullable enable
using System;
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

/// <summary>
/// Generic <see cref="IBppMountable"/> for the common case: add one component to the host at
/// mount, run an optional initializer, and DestroyImmediate it on unmount. Replaces the
/// per-feature mount classes that differed only by component type and initializer call.
/// </summary>
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
