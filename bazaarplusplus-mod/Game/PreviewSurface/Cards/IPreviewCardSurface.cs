using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal interface IPreviewCardSurface
{
    Task<GameObject> CreateAsync(PreviewCardSpec spec, Transform parent);

    Task UpdateAsync(GameObject cardObject, PreviewCardSpec spec);

    void Destroy(GameObject cardObject);
}
