using System.Buffers;
using Il2CppBazaarGameShared;
using Il2CppInterop.Runtime.InteropTypes;
using MessagePack;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Replay Payload's message bytes on the IL2CPP game. A message here is a proxy, which no
/// managed serializer can read, so the bytes come from the game's own IL2CPP
/// <c>MessagePackSerializer</c> with <c>MessagePackConfig.Options</c>: the call the desktop's
/// <c>DesktopPvpBattleReads.KeepReplayPart</c> makes, with the concrete message type as the
/// generic argument.
/// </summary>
internal static class AndroidReplayMessages
{
    /// <summary>
    /// Serializes <paramref name="message"/> and decodes the bytes back as <typeparamref name="T"/>,
    /// so a payload is only kept when the game's own serializer reads it. Main thread only: it
    /// calls into the IL2CPP runtime.
    /// </summary>
    public static byte[] Serialize<T>(T message, Func<T, string?> messageId)
        where T : Il2CppObjectBase
    {
        ArgumentNullException.ThrowIfNull(message);
        var options = MessagePackConfig.Options;
        var serialized =
            Il2CppMessagePack.MessagePackSerializer.Serialize(
                message,
                options,
                Il2CppSystem.Threading.CancellationToken.None
            ) ?? throw new InvalidOperationException("The game's serializer returned no bytes.");
        var bytes = serialized.AsSpan().ToArray();

        var stream = new Il2CppSystem.IO.MemoryStream(serialized);
        var decoded =
            Il2CppMessagePack.MessagePackSerializer.Deserialize<T>(
                stream.Cast<Il2CppSystem.IO.Stream>(),
                options,
                Il2CppSystem.Threading.CancellationToken.None
            ) ?? throw new InvalidDataException("The game's serializer decoded no message.");
        if (!string.Equals(messageId(decoded), messageId(message), StringComparison.Ordinal))
            throw new InvalidDataException("The decoded message has another message id.");
        return bytes;
    }

    /// <summary>
    /// Decodes Replay Payload bytes back into the game's message with the game's own serializer,
    /// the read the desktop's <c>CombatReplayLoader</c> makes. Main thread only.
    /// </summary>
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

    /// <summary>
    /// Whether <paramref name="bytes"/> hold exactly one well-formed MessagePack value. This is the
    /// check the Bundle composer runs off the main thread, where the IL2CPP serializer may not be
    /// called; the full decode already ran when the payload was captured.
    /// </summary>
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
