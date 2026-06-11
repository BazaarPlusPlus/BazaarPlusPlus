#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppMountableRegistry
{
    private readonly List<IBppMountable> _mountables = new();

    public void Register(IBppMountable mountable) => _mountables.Add(mountable);

    public void MountAll(GameObject host, IBppServices services)
    {
        foreach (var m in _mountables)
            m.Mount(host, services);
    }

    public void UnmountAll(GameObject host)
    {
        for (var i = _mountables.Count - 1; i >= 0; i--)
            _mountables[i].Unmount(host);
    }
}
