#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class ChineseLocaleModeProvider : ILocaleModeProvider
{
    private readonly IBppConfig _config;

    internal ChineseLocaleModeProvider(IBppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public BppChineseLocaleMode CurrentMode =>
        ChineseScriptConverter.NormalizeMode(
            _config.ChineseLocaleModeConfig?.Value ?? BppChineseLocaleMode.Mainland
        );
}
