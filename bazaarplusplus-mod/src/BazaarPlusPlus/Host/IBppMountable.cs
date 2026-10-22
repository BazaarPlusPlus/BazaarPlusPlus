#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Host;

/// <summary>Unity-aware counterpart of <see cref="IBppFeature"/>. Mount-time
/// receives the host <c>GameObject</c> so implementors can <c>AddComponent</c>.</summary>
internal interface IBppMountable
{
    void Mount(GameObject host, IBppServices services);
    void Unmount(GameObject host);
}
