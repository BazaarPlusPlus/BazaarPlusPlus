#nullable enable
using System.Reflection;

var modAssembly = Assembly.Load("BazaarPlusPlus");
var confirmationType = RequireType("BazaarPlusPlus.Game.HistoryPanel.DeleteConfirmation");
var decisionsType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelDecisions");
var buttonModelType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelButtonModel");
var runType = RequireType("BazaarPlusPlus.Game.HistoryPanel.Data.HistoryRunRecord");
var gameBuildChannelType = RequireType("BazaarPlusPlus.Core.Runtime.GameBuildChannel");

TestDeleteConfirmationFiveSecondArm();
TestCanDeleteRunDecisionArms();
TestAccountLinkCardAvailabilityGate();
TestDatabaseChipTextAndSeverity();
TestButtonModelReplayRecordDeleteParity();

Console.WriteLine("HistoryPanelDecisions checks passed.");

void TestDeleteConfirmationFiveSecondArm()
{
    var confirmation = CreateConfirmation("run-1", 105f);

    Assert(GetString(confirmation, "RunId") == "run-1", "Delete confirmation should store run id.");
    Assert(
        GetFloat(confirmation, "ExpiresAt") == 105f,
        "Delete confirmation arm should be now + 5s."
    );
    Assert(
        InvokeBool(confirmationType, confirmation, "IsActiveFor", "run-1", 100f),
        "Confirmation should be active for the armed run before expiry."
    );
    Assert(
        InvokeBool(confirmationType, confirmation, "IsActiveFor", "run-1", 104.999f),
        "Confirmation should stay active until expiry."
    );
    Assert(
        !InvokeBool(confirmationType, confirmation, "IsActiveFor", "run-2", 104f),
        "Confirmation should not apply to a different run."
    );
    Assert(
        !InvokeBool(confirmationType, confirmation, "HasExpired", 104.999f),
        "Confirmation should not expire before ExpiresAt."
    );
    Assert(
        !InvokeBool(confirmationType, confirmation, "IsActiveFor", "run-1", 105f),
        "Confirmation should stop being active at ExpiresAt."
    );
    Assert(
        InvokeBool(confirmationType, confirmation, "HasExpired", 105f),
        "Confirmation should expire at ExpiresAt."
    );
    Assert(
        !InvokeBool(confirmationType, CreateConfirmation(null, 0f), "HasExpired", 100f),
        "Empty confirmation should not count as expired."
    );
}

void TestCanDeleteRunDecisionArms()
{
    AssertCanDelete(
        sectionMode: "Ghost",
        selectedRun: CreateRun("run-1", "finished"),
        isInGameRun: false,
        currentServerRunId: null,
        isRepositoryAvailable: true,
        expectedAllowed: false,
        expectedReason: "Ghost battles cannot be deleted from this panel yet."
    );
    AssertCanDelete(
        sectionMode: "Runs",
        selectedRun: null,
        isInGameRun: false,
        currentServerRunId: null,
        isRepositoryAvailable: true,
        expectedAllowed: false,
        expectedReason: "Select a run to delete."
    );
    AssertCanDelete(
        sectionMode: "Runs",
        selectedRun: CreateRun("run-1", "active"),
        isInGameRun: false,
        currentServerRunId: null,
        isRepositoryAvailable: true,
        expectedAllowed: false,
        expectedReason: "Active runs cannot be deleted."
    );
    AssertCanDelete(
        sectionMode: "Runs",
        selectedRun: CreateRun("run-1", "finished"),
        isInGameRun: true,
        currentServerRunId: "run-1",
        isRepositoryAvailable: true,
        expectedAllowed: false,
        expectedReason: "The currently active gameplay run cannot be deleted."
    );
    AssertCanDelete(
        sectionMode: "Runs",
        selectedRun: CreateRun("run-1", "finished"),
        isInGameRun: false,
        currentServerRunId: null,
        isRepositoryAvailable: false,
        expectedAllowed: false,
        expectedReason: "Run log repository is unavailable."
    );
    AssertCanDelete(
        sectionMode: "Runs",
        selectedRun: CreateRun("run-1", "finished"),
        isInGameRun: true,
        currentServerRunId: "other-run",
        isRepositoryAvailable: true,
        expectedAllowed: true,
        expectedReason: string.Empty
    );
}

void TestDatabaseChipTextAndSeverity()
{
    AssertChip(false, false, "DB Unavailable", "Failure");
    AssertChip(true, false, "DB Missing", "Neutral");
    AssertChip(true, true, "DB Connected", "Success");
}

void TestAccountLinkCardAvailabilityGate()
{
    AssertAccountLinkCardAvailable(true, "Online", true);
    AssertAccountLinkCardAvailable(true, "Unknown", true);
    AssertAccountLinkCardAvailable(true, "Ptr", false);
    AssertAccountLinkCardAvailable(false, "Online", false);
}

void TestButtonModelReplayRecordDeleteParity()
{
    var label = "Replay Label";

    AssertButtons(
        BuildButtons(false, true, "", label, false, true, false, true),
        replayText: label,
        replayEnabled: true,
        recordText: "Record",
        recordEnabled: true,
        deleteText: "Delete",
        deleteEnabled: true
    );
    AssertButtons(
        BuildButtons(false, false, "", label, true, true, false, true),
        replayText: "In Run",
        replayEnabled: false,
        recordText: "Record",
        recordEnabled: true,
        deleteText: "Delete",
        deleteEnabled: true
    );
    AssertButtons(
        BuildButtons(false, false, "", label, false, false, true, true),
        replayText: label,
        replayEnabled: false,
        recordText: "Record",
        recordEnabled: false,
        deleteText: "Sure?",
        deleteEnabled: true
    );
    AssertButtons(
        BuildButtons(false, false, "missing replay", label, false, true, false, false),
        replayText: "Unavailable",
        replayEnabled: false,
        recordText: "Record",
        recordEnabled: true,
        deleteText: "Delete",
        deleteEnabled: false
    );
    AssertButtons(
        BuildButtons(true, true, "", label, false, true, false, true),
        replayText: "Working...",
        replayEnabled: false,
        recordText: "Record",
        recordEnabled: false,
        deleteText: "Delete",
        deleteEnabled: true
    );
}

object CreateConfirmation(string? runId, float expiresAt)
{
    return Activator.CreateInstance(confirmationType, runId, expiresAt)
        ?? throw new InvalidOperationException("DeleteConfirmation should construct.");
}

object CreateRun(string runId, string rawStatus)
{
    return Activator.CreateInstance(
            runType,
            runId,
            "Vanessa",
            "Ranked",
            DateTimeOffset.Parse("2026-06-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-19T00:10:00Z"),
            DateTimeOffset.Parse("2026-06-19T00:10:00Z"),
            3,
            1,
            40,
            0,
            5,
            1,
            10,
            "Bronze",
            100,
            1,
            0,
            rawStatus
        ) ?? throw new InvalidOperationException("HistoryRunRecord should construct.");
}

void AssertCanDelete(
    string sectionMode,
    object? selectedRun,
    bool isInGameRun,
    string? currentServerRunId,
    bool isRepositoryAvailable,
    bool expectedAllowed,
    string expectedReason
)
{
    var parameters = new[]
    {
        Enum.Parse(RequireType("BazaarPlusPlus.Game.HistoryPanel.HistorySectionMode"), sectionMode),
        selectedRun,
        isInGameRun,
        currentServerRunId,
        isRepositoryAvailable,
        null,
    };
    var result = InvokeStatic(decisionsType, "CanDeleteRun", parameters);
    Assert(
        result is bool allowed && allowed == expectedAllowed,
        $"CanDeleteRun {sectionMode} result."
    );
    Assert((string?)parameters[5] == expectedReason, $"CanDeleteRun {sectionMode} reason.");
}

void AssertChip(bool isAvailable, bool databaseExists, string expectedText, string expectedSeverity)
{
    var chip = InvokeStatic(decisionsType, "ResolveDatabaseChip", isAvailable, databaseExists);
    Assert(GetString(chip, "Text") == expectedText, $"Database chip text {expectedText}.");
    Assert(
        GetEnumName(chip, "Severity") == expectedSeverity,
        $"Database chip severity {expectedSeverity}."
    );
}

void AssertAccountLinkCardAvailable(bool dataSharingEnabled, string channel, bool expectedAvailable)
{
    var result = InvokeStatic(
        decisionsType,
        "IsAccountLinkCardAvailable",
        dataSharingEnabled,
        Enum.Parse(gameBuildChannelType, channel)
    );
    Assert(
        result is bool available && available == expectedAvailable,
        $"Account link card availability sharing={dataSharingEnabled} channel={channel}."
    );
}

object BuildButtons(
    bool replayActionInProgress,
    bool canReplaySelectedBattle,
    string replayUnavailableReason,
    string replayActionLabel,
    bool isInGameRun,
    bool canRecordSelectedBattle,
    bool isDeleteConfirmationActive,
    bool canDeleteSelectedRun
)
{
    return InvokeStatic(
        buttonModelType,
        "Build",
        replayActionInProgress,
        canReplaySelectedBattle,
        replayUnavailableReason,
        replayActionLabel,
        isInGameRun,
        canRecordSelectedBattle,
        isDeleteConfirmationActive,
        canDeleteSelectedRun
    );
}

void AssertButtons(
    object model,
    string replayText,
    bool replayEnabled,
    string recordText,
    bool recordEnabled,
    string deleteText,
    bool deleteEnabled
)
{
    Assert(
        GetString(model, "ReplayButtonText") == replayText,
        $"Replay text should be {replayText}."
    );
    Assert(
        GetBool(model, "ReplayButtonEnabled") == replayEnabled,
        $"Replay enabled should be {replayEnabled}."
    );
    Assert(
        GetString(model, "RecordAndReplayButtonText") == recordText,
        $"Record text should be {recordText}."
    );
    Assert(
        GetBool(model, "RecordAndReplayButtonEnabled") == recordEnabled,
        $"Record enabled should be {recordEnabled}."
    );
    Assert(
        GetString(model, "DeleteButtonText") == deleteText,
        $"Delete text should be {deleteText}."
    );
    Assert(
        GetBool(model, "DeleteButtonEnabled") == deleteEnabled,
        $"Delete enabled should be {deleteEnabled}."
    );
}

Type RequireType(string fullName)
{
    return modAssembly.GetType(fullName, throwOnError: true)!;
}

object InvokeStatic(Type type, string methodName, params object?[] args)
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

bool InvokeBool(Type type, object target, string methodName, params object?[] args)
{
    var method =
        type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        ) ?? throw new InvalidOperationException($"{type.FullName}.{methodName} should exist.");
    return method.Invoke(target, args) is bool value
        ? value
        : throw new InvalidOperationException($"{type.FullName}.{methodName} should return bool.");
}

string? GetString(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) as string;
}

float GetFloat(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) is float value
        ? value
        : throw new InvalidOperationException($"{propertyName} should be a float.");
}

bool GetBool(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) is bool value
        ? value
        : throw new InvalidOperationException($"{propertyName} should be a bool.");
}

string GetEnumName(object target, string propertyName)
{
    var value = GetProperty(target, propertyName).GetValue(target);
    return value?.ToString()
        ?? throw new InvalidOperationException($"{propertyName} should not be null.");
}

PropertyInfo GetProperty(object target, string propertyName)
{
    return target.GetType().GetProperty(propertyName)
        ?? throw new InvalidOperationException(
            $"{propertyName} should exist on {target.GetType()}."
        );
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
