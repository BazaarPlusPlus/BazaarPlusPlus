#nullable enable
using System.Reflection;
using BazaarPlusPlus.Localization;

internal static class AccountLinkCardTests
{
    public static void Run(Assembly assembly)
    {
        var decisions = assembly.GetType(
            "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDecisions",
            true
        )!;
        var channelType = assembly.GetType("BazaarPlusPlus.Core.Runtime.GameBuildChannel", true)!;
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var resolve = decisions.GetMethod("ResolveAccountLinkCard", flags)!;
        var gate = decisions.GetMethod("ResolveAccountLinkGate", flags)!;
        var settingLabel = assembly
            .GetType("BazaarPlusPlus.Game.Screenshots.BazaarDbBundleSettingsMenuLabel", true)!
            .GetMethod("Resolve", flags)!;
        try
        {
            foreach (var language in new[] { "en", "zh-Hans" })
            {
                L.Install(new Language(language), new Mainland());
                var chinese = language == "zh-Hans";
                var label = (string)settingLabel.Invoke(null, [language])!;
                foreach (var channel in new[] { "Online", "Unknown", "Ptr" })
                foreach (var uploads in new[] { false, true })
                foreach (var hasAccount in new[] { false, true })
                foreach (var linked in new[] { false, true })
                foreach (var expanded in new[] { false, true })
                foreach (var inProgress in new[] { false, true })
                {
                    var channelValue = Enum.Parse(channelType, channel);
                    var gateValue = gate.Invoke(null, [uploads, channelValue])!;
                    var card = resolve.Invoke(
                        null,
                        [gateValue, hasAccount, linked, expanded, inProgress]
                    )!;
                    var status = (string)Property(card, "StatusText");
                    var persistence = (string)Property(card, "PersistenceText");
                    var actionVisible = (bool)Property(card, "ActionVisible");
                    var formVisible = (bool)Property(card, "FormVisible");
                    var rowAction = (string)Property(card, "RowActionText");
                    var linkButton = (string)Property(card, "LinkButtonText");
                    var alreadyLinkedVisible = (bool)Property(card, "AlreadyLinkedButtonVisible");
                    var linkButtonEnabled = (bool)Property(card, "LinkButtonEnabled");
                    var inputEnabled = (bool)Property(card, "InputEnabled");
                    var canAct = uploads && channel != "Ptr" && hasAccount;
                    Require(
                        actionVisible == canAct,
                        "Link actions must obey uploads, channel and account gates."
                    );
                    Require(
                        formVisible == (canAct && expanded),
                        "A previously expanded form must disappear when uploads or the account become unavailable."
                    );
                    Require(status.Length > 0, "Every card state must have a visible explanation.");
                    Require(
                        (persistence.Length > 0) == (canAct && linked),
                        "Only an available linked row should add persistence guidance, regardless of expansion."
                    );
                    Require(
                        rowAction
                            == (
                                linked
                                    ? (chinese ? "重新绑定" : "Re-link")
                                    : (chinese ? "绑定…" : "Link…")
                            ),
                        "The row action offers re-linking exactly when the account is linked."
                    );
                    Require(
                        linkButton
                            == (
                                inProgress
                                    ? (chinese ? "绑定中..." : "Linking...")
                                    : (chinese ? "绑定账号" : "Link account")
                            ),
                        "The link button shows progress while a link request is running."
                    );
                    Require(
                        alreadyLinkedVisible == (formVisible && !linked && !inProgress),
                        "The already-linked shortcut belongs to an open form of an unlinked, idle account."
                    );
                    Require(
                        linkButtonEnabled == (canAct && !inProgress),
                        "The link button accepts input only when linking is possible and idle."
                    );
                    Require(
                        inputEnabled == linkButtonEnabled,
                        "The code input and link button enable together."
                    );
                    if (channel == "Ptr")
                        Contains(status, chinese ? "PTR 不支持" : "PTR does not support");
                    else if (!uploads)
                    {
                        Contains(status, label);
                        Contains(status, chinese ? "设置栏" : "settings dock");
                    }
                    else if (!hasAccount)
                        Contains(status, chinese ? "登录" : "Sign in");
                    else if (linked)
                    {
                        Contains(status, chinese ? "已绑定" : "Linked");
                        Contains(persistence, chinese ? "BazaarDB 账号" : "BazaarDB account");
                        Contains(
                            persistence,
                            chinese ? "更新插件无需重新绑定" : "Updating the mod keeps it"
                        );
                    }
                    else
                        Contains(status, chinese ? "未绑定" : "not linked");
                }
            }
        }
        finally
        {
            L.Install(new Language("en"), new Mainland());
        }
    }

    private static object Property(object value, string name) =>
        value.GetType().GetProperty(name)!.GetValue(value)!;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Contains(string value, string expected) =>
        Require(
            value.Contains(expected, StringComparison.Ordinal),
            $"Expected '{expected}' in '{value}'."
        );

    private sealed class Language(string code) : ILanguageProvider
    {
        public string CurrentLanguageCode => code;
    }

    private sealed class Mainland : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
