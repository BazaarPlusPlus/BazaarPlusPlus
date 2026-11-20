#nullable enable
using BazaarPlusPlus.Core.Runtime;
using UnityEngine;

namespace BazaarPlusPlus.Host;

internal interface IBppMountable
{
    void Mount(GameObject host, IBppServices services);
    void Unmount(GameObject host);
}
