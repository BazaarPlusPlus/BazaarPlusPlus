#pragma warning disable CS0436
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal interface IPreviewCardFactory
{
    Task<GameObject> CreateCardAsync(PreviewCardSpec spec, Transform parent);

    Task UpdateCardAsync(GameObject cardObject, PreviewCardSpec spec);

    void DestroyCard(GameObject cardObject);
}
