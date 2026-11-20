#nullable enable
namespace BazaarPlusPlus.Storage.RunLog;

// These rows upload in the Run segment's dedicated message stream (ADR-0028).
public static class RunMessageStreamKinds
{
    public const string Message = "stream_message";
    public const string Command = "stream_command";

    public static bool Contains(string kind) => kind is Message or Command;
}
