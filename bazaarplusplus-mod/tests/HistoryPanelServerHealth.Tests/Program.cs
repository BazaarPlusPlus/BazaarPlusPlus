#nullable enable
using System.Reflection;
using BazaarPlusPlus.Localization;

var formatterType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelServerHealthFormatter"
);
var probeResultType = RequireModApiType("BazaarPlusPlus.ModApi.Clients.ModApiHealthProbeResult");

var idle = InvokeStatic(formatterType, "Idle");
Assert(GetString(idle, "ButtonText") == "Check Server", "Idle button should invite a probe.");
Assert(GetBool(idle, "ButtonEnabled"), "Idle button should be enabled.");
Assert(GetNullableString(idle, "StatusMessage") == null, "Idle status should stay quiet.");

var checking = InvokeStatic(formatterType, "Checking");
Assert(GetString(checking, "ButtonText") == "Checking...", "Checking button should show progress.");
Assert(!GetBool(checking, "ButtonEnabled"), "Checking button should be disabled.");
Assert(
    GetString(checking, "StatusMessage") == "Checking game-server connectivity...",
    "Checking status should explain what is happening."
);

var success = InvokeStatic(
    probeResultType,
    "Success",
    new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
    142L,
    "ok",
    new DateTime(2026, 6, 3, 0, 0, 1, DateTimeKind.Utc)
);
var successDisplay = InvokeStatic(formatterType, "FromProbeResult", success);
Assert(GetString(successDisplay, "ButtonText") == "Check Server", "Success returns to idle label.");
Assert(GetBool(successDisplay, "ButtonEnabled"), "Success should re-enable the button.");
Assert(
    GetString(successDisplay, "StatusMessage") == "Game and server connected in 142 ms.",
    "Success status should include RTT."
);

var failure = InvokeStatic(
    probeResultType,
    "Failure",
    new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
    87L,
    "http_503"
);
var failureDisplay = InvokeStatic(formatterType, "FromProbeResult", failure);
Assert(GetString(failureDisplay, "ButtonText") == "Check Server", "Failure returns to idle label.");
Assert(GetBool(failureDisplay, "ButtonEnabled"), "Failure should re-enable the button.");
Assert(
    GetString(failureDisplay, "StatusMessage") == "Game-server check failed after 87 ms: http_503",
    "Failure status should include RTT and stable error."
);

var textType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelText");
var languageProvider = new MutableLanguageProvider("zh-CN");
var modeProvider = new MutableLocaleModeProvider(BppChineseLocaleMode.Mainland);
L.Install(languageProvider, modeProvider);
var accountLinkType =
    textType.GetNestedType("AccountLink", BindingFlags.NonPublic | BindingFlags.Public)
    ?? throw new InvalidOperationException("HistoryPanelText.AccountLink should exist.");

languageProvider.CurrentLanguageCode = string.Empty;
modeProvider.CurrentMode = BppChineseLocaleMode.Mainland;
Assert(
    InvokeStatic(accountLinkType, "Title") as string == "Link BazaarDB account",
    "Account link title should resolve in English."
);
Assert(
    InvokeStatic(accountLinkType, "Linked") as string == "Linked to BazaarDB",
    "Linked row text should be name-less in English."
);
Assert(
    InvokeStatic(accountLinkType, "NotLinked") as string == "BazaarDB not linked",
    "Not linked row text should resolve in English."
);
Assert(
    InvokeStatic(accountLinkType, "RowBind") as string == "Link…",
    "Collapsed row bind action should resolve in English."
);
Assert(
    InvokeStatic(accountLinkType, "Collapse") as string == "Hide",
    "Collapse action should resolve in English."
);
Assert(
    InvokeStatic(accountLinkType, "AlreadyLinkedElsewhereButton") as string == "I already linked",
    "Manual already-linked action should resolve in English."
);
Assert(
    InvokeStatic(accountLinkType, "InvalidOrExpired") as string
        == "Code invalid or expired - generate a new one",
    "Invalid code text should use repo-compatible ASCII punctuation."
);

languageProvider.CurrentLanguageCode = "zh-CN";
modeProvider.CurrentMode = BppChineseLocaleMode.Mainland;
Assert(
    InvokeStatic(accountLinkType, "Linked") as string == "已绑定 BazaarDB",
    "Linked row text should resolve simplified Chinese, name-less."
);
Assert(
    InvokeStatic(accountLinkType, "NotLinked") as string == "BazaarDB 未绑定",
    "Not linked row text should resolve simplified Chinese."
);
Assert(
    InvokeStatic(accountLinkType, "RowBind") as string == "绑定…",
    "Collapsed row bind action should resolve simplified Chinese."
);
Assert(
    InvokeStatic(accountLinkType, "Collapse") as string == "收起",
    "Collapse action should resolve simplified Chinese."
);
Assert(
    InvokeStatic(accountLinkType, "AlreadyLinkedElsewhereButton") as string == "我已绑定",
    "Manual already-linked action should resolve simplified Chinese."
);
Assert(
    InvokeStatic(accountLinkType, "Offline") as string == "无法连接 BazaarDB，请检查网络",
    "Offline error should resolve simplified Chinese."
);

modeProvider.CurrentMode = BppChineseLocaleMode.Taiwan;
Assert(
    InvokeStatic(accountLinkType, "Relink") as string == "重新綁定",
    "Relink should resolve traditional Chinese when Taiwan mode is active."
);
Assert(
    InvokeStatic(accountLinkType, "NotLinked") as string == "BazaarDB 未綁定",
    "Not linked row text should resolve traditional Chinese when Taiwan mode is active."
);
Assert(
    InvokeStatic(accountLinkType, "RowBind") as string == "綁定…",
    "Collapsed row bind action should resolve traditional Chinese when Taiwan mode is active."
);
Assert(
    InvokeStatic(accountLinkType, "Collapse") as string == "收起",
    "Collapse action should resolve traditional Chinese when Taiwan mode is active."
);
Assert(
    InvokeStatic(accountLinkType, "AlreadyLinkedElsewhereButton") as string == "我已綁定",
    "Manual already-linked action should resolve traditional Chinese when Taiwan mode is active."
);
Assert(
    InvokeStatic(accountLinkType, "ServerBusy") as string == "BazaarDB 暫時無法使用，請稍後重試",
    "ServerBusy should resolve traditional Chinese when Taiwan mode is active."
);

var storeType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.AccountLink.BazaarDbAccountLinkStore"
);
Assert(
    InvokeStatic(storeType, "BuildPrefsKey", "player@example.com") as string
        == "BPP.HistoryPanel.BazaarDbLinkedName.player%40example.com",
    "Account link store should escape account-scoped PlayerPrefs keys."
);
Assert(
    InvokeStatic(storeType, "BuildPrefsKey", "   ") as string
        == "BPP.HistoryPanel.BazaarDbLinkedName.anonymous",
    "Account link store should use anonymous scope for blank account ids."
);

// Contract rule: only a 200 Linked outcome may persist the local "linked" hint. 409/AlreadyLinked
// is a DIFFERENT BazaarDB user and every error outcome must leave the account unlinked. This guards
// the coordinator's redeem mapping (the highest-value, contract-sensitive branch) behaviorally.
var coordinatorType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelCoordinator");
var outcomeType = RequireModApiType("BazaarPlusPlus.ModApi.Clients.BazaarDbLinkOutcome");

Assert(
    (bool)InvokeStatic(coordinatorType, "OutcomeConfirmsLink", Enum.Parse(outcomeType, "Linked")),
    "A 200 Linked outcome should confirm the account link."
);
foreach (
    var failing in new[]
    {
        "AlreadyLinked",
        "InvalidOrExpired",
        "MissingFields",
        "ServerError",
        "Transport",
    }
)
{
    Assert(
        !(bool)InvokeStatic(
            coordinatorType,
            "OutcomeConfirmsLink",
            Enum.Parse(outcomeType, failing)
        ),
        $"{failing} must not confirm the link (contract: 409 is a different user; errors never link)."
    );
}

Assert(
    InvokeStatic(coordinatorType, "RedeemBannerSeverity", Enum.Parse(outcomeType, "Linked"))
        .ToString() == "Success",
    "Linked should surface a success banner."
);
Assert(
    InvokeStatic(coordinatorType, "RedeemBannerSeverity", Enum.Parse(outcomeType, "AlreadyLinked"))
        .ToString() == "Failure",
    "AlreadyLinked should surface a failure banner, never a success."
);

Console.WriteLine("HistoryPanelServerHealth checks passed.");

static Type RequireType(string fullName)
{
    var assembly = LoadAssembly("BazaarPlusPlus");
    return assembly.GetType(fullName)
        ?? throw new InvalidOperationException($"{fullName} should exist.");
}

static Type RequireModApiType(string fullName)
{
    var assembly = LoadAssembly("BazaarPlusPlus.ModApi");
    return assembly.GetType(fullName)
        ?? throw new InvalidOperationException($"{fullName} should exist.");
}

static Assembly LoadAssembly(string assemblyName)
{
    return AppDomain
            .CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)
        ?? Assembly.Load(assemblyName);
}

static object InvokeStatic(Type type, string methodName, params object[] args)
{
    var method =
        type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"{type.FullName}.{methodName} should exist.");

    return method.Invoke(null, args)
        ?? throw new InvalidOperationException(
            $"{type.FullName}.{methodName} should return a value."
        );
}

static string GetString(object target, string propertyName)
{
    return GetNullableString(target, propertyName)
        ?? throw new InvalidOperationException($"{propertyName} should not be null.");
}

static string? GetNullableString(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) as string;
}

static bool GetBool(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) is bool value
        ? value
        : throw new InvalidOperationException($"{propertyName} should be a bool.");
}

static PropertyInfo GetProperty(object target, string propertyName)
{
    return target.GetType().GetProperty(propertyName)
        ?? throw new InvalidOperationException($"{propertyName} should exist.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class MutableLanguageProvider(string languageCode) : ILanguageProvider
{
    public string CurrentLanguageCode { get; set; } = languageCode;
}

internal sealed class MutableLocaleModeProvider(BppChineseLocaleMode mode) : ILocaleModeProvider
{
    public BppChineseLocaleMode CurrentMode { get; set; } = mode;
}
