#nullable enable
using System.Reflection;
using BazaarPlusPlus.Localization;
using Xunit;

public sealed class AccountLinkCardTests
{
    // The capsule ran these checks with English installed.
    public AccountLinkCardTests() => L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

    [Fact]
    public void ResolvesTheCardForEveryGateAndLanguage()
    {
        var assembly = typeof(BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDecisions).Assembly;
        var decisions = assembly.GetType(
            "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDecisions",
            true
        )!;
        var channelType = typeof(BazaarPlusPlus.Core.Runtime.GameBuildChannel);
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
                L.Install(() => language, () => BppChineseLocaleMode.Mainland);
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
                        (persistence.Length > 0) == canAct,
                        "Available accounts explain that linking follows the game account, regardless of expansion."
                    );
                    Require(
                        rowAction
                            == (
                                linked
                                    ? (chinese ? "重新绑定" : "Re-link")
                                    : (chinese ? "输入绑定码" : "Enter code")
                            ),
                        "The row action offers re-linking exactly when the account is linked."
                    );
                    Require(
                        linkButton
                            == (
                                inProgress
                                    ? (chinese ? "绑定中..." : "Linking...")
                                    : (chinese ? "确认绑定" : "Confirm link")
                            ),
                        "The link button shows progress while a link request is running."
                    );
                    Require(
                        alreadyLinkedVisible == (canAct && !linked && !inProgress),
                        "An unconfirmed idle account can confirm an existing link without opening the code form."
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
                        Contains(status, chinese ? "PTR 和 staging" : "PTR and staging");
                    else if (!uploads)
                    {
                        Contains(status, label);
                        Contains(
                            status,
                            chinese ? "BazaarPlusPlus 设置" : "BazaarPlusPlus settings"
                        );
                    }
                    else if (!hasAccount)
                        Contains(status, chinese ? "登录" : "Sign in");
                    else if (linked)
                    {
                        Contains(status, chinese ? "已绑定" : "Linked");
                        Contains(persistence, chinese ? "游戏账号" : "game account");
                        Contains(persistence, chinese ? "换设备无需重绑" : "another device");
                    }
                    else
                    {
                        Contains(status, chinese ? "尚未确认绑定" : "not confirmed");
                        Contains(persistence, chinese ? "已经绑定" : "Already linked");
                    }
                }
            }
        }
        finally
        {
            L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
        }
    }

    private static object Property(object value, string name) =>
        value.GetType().GetProperty(name)!.GetValue(value)!;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            Xunit.Assert.Fail(message);
    }

    private static void Contains(string value, string expected) =>
        Require(
            value.Contains(expected, StringComparison.Ordinal),
            $"Expected '{expected}' in '{value}'."
        );
}
