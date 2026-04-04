#pragma warning disable CS0436
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterDatabasePreviewDataSource : IPreviewDataSource
{
    private readonly string _encounterId;

    public MonsterDatabasePreviewDataSource(string encounterId)
    {
        _encounterId = encounterId ?? string.Empty;
    }

    public bool TryBuild(out PreviewBoardModel model)
    {
        model = null;
        if (!BppRuntimeHost.MonsterCatalog.TryGetByEncounterId(_encounterId, out var monster))
            return false;

        model = MonsterPreviewProjector.BuildModel(monster, "monster_db");
        return model.ItemCards.Count > 0 || model.SkillCards.Count > 0;
    }
}
