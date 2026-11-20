#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Background-only Media Foundation bridge. H.264 samples are copied without re-encoding while
/// the single endpoint-loopback WAV is converted to stereo 48 kHz AAC.
/// </summary>
internal static class WindowsNativeReplayAudioMuxer
{
    internal const int TimeoutResultCode = -8;

    internal readonly record struct Result(int ResultCode, string Error)
    {
        internal bool Succeeded => ResultCode == 1;
        internal bool TimedOut => ResultCode == TimeoutResultCode;
    }

    internal static Result Mux(
        string silentVideoPath,
        IReadOnlyList<string> wavPaths,
        string finalPath,
        int audioBitrateKbps,
        TimeSpan timeout
    )
    {
        if (string.IsNullOrWhiteSpace(silentVideoPath))
            throw new ArgumentException(
                "A silent video path is required.",
                nameof(silentVideoPath)
            );
        if (wavPaths == null || wavPaths.Count != 1)
        {
            return new Result(
                -2,
                "The Windows native mux currently requires exactly one WASAPI WAV track."
            );
        }
        if (string.IsNullOrWhiteSpace(finalPath))
            throw new ArgumentException("A final video path is required.", nameof(finalPath));

        var wavPath = IntPtr.Zero;
        var nativePathArray = IntPtr.Zero;
        try
        {
            wavPath = AllocateUtf8(wavPaths[0]);
            nativePathArray = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(nativePathArray, wavPath);
            var error = new StringBuilder(2048);
            var timeoutMs = (int)Math.Max(1, Math.Min(int.MaxValue, timeout.TotalMilliseconds));
            var result = BppMfMuxAudio(
                silentVideoPath,
                nativePathArray,
                1,
                finalPath,
                checked(Math.Max(1, audioBitrateKbps) * 1000),
                timeoutMs,
                error,
                error.Capacity
            );
            return new Result(result, error.ToString());
        }
        catch (Exception ex)
            when (ex
                    is DllNotFoundException
                        or EntryPointNotFoundException
                        or BadImageFormatException
            )
        {
            return new Result(-10, ex.Message);
        }
        finally
        {
            if (nativePathArray != IntPtr.Zero)
                Marshal.FreeHGlobal(nativePathArray);
            if (wavPath != IntPtr.Zero)
                Marshal.FreeHGlobal(wavPath);
        }
    }

    private static IntPtr AllocateUtf8(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A WAV path is required.", nameof(value));
        var bytes = Encoding.UTF8.GetBytes(value + '\0');
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    [DllImport(
        WindowsMediaFoundationVideoEncoder.NativeLibrary,
        CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi
    )]
    private static extern int BppMfMuxAudio(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string silentVideoPath,
        IntPtr wavPaths,
        int wavPathCount,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string finalPath,
        int audioBitrateBitsPerSecond,
        int timeoutMs,
        StringBuilder errorBuffer,
        int errorCapacity
    );
}
