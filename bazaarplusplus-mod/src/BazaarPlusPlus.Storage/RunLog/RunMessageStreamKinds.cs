#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

/// <summary>
/// The <c>run_events</c> kinds that hold the Run Message Stream (ADR-0028). They are uploaded in
/// the Run segment's message stream, not with the other events, and deleted once the run can no
/// longer upload them (<c>BundleQueueStore.DeleteSpentMessageStreams</c>).
/// </summary>
public static class RunMessageStreamKinds
{
    public const string Message = "stream_message";
    public const string Command = "stream_command";

    public static bool Contains(string kind) => kind is Message or Command;
}
