using System.Buffers;
using Il2CppBazaarGameShared;
using Il2CppInterop.Runtime.InteropTypes;
using MessagePack;

namespace BazaarPlusPlus.Android.GameInterop;

// A managed serializer cannot read IL2CPP message proxies. Use the game's serializer with the
// concrete message type.
internal static class AndroidReplayMessages
{
    public static byte[] Serialize<T>(T message)
        where T : Il2CppObjectBase
    {
        ArgumentNullException.ThrowIfNull(message);
        var serialized =
            Il2CppMessagePack.MessagePackSerializer.Serialize(
                message,
                MessagePackConfig.Options,
                Il2CppSystem.Threading.CancellationToken.None
            ) ?? throw new InvalidOperationException("The game's serializer returned no bytes.");
        return serialized.AsSpan().ToArray();
    }

    public static T Deserialize<T>(byte[] bytes, string part)
        where T : Il2CppObjectBase
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidDataException($"Replay message '{part}' is empty.");
        var stream = new Il2CppSystem.IO.MemoryStream(
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(bytes)
        );
        return Il2CppMessagePack.MessagePackSerializer.Deserialize<T>(
                stream.Cast<Il2CppSystem.IO.Stream>(),
                MessagePackConfig.Options,
                Il2CppSystem.Threading.CancellationToken.None
            ) ?? throw new InvalidDataException($"Replay message '{part}' decoded to nothing.");
    }

    public static void VerifyWellFormed(byte[] bytes, string label)
    {
        if (bytes == null || bytes.Length == 0)
            throw new InvalidDataException($"Replay message '{label}' is empty.");
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.Skip();
        if (!reader.End)
            throw new InvalidDataException($"Replay message '{label}' has trailing bytes.");
    }
}
