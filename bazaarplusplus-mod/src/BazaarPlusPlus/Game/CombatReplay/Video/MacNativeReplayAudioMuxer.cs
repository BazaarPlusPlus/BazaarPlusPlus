#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Background-only AVFoundation bridge for the macOS finalize pass. H.264 samples are copied
/// without re-encoding while the captured PCM WAV tracks are mixed and encoded to AAC.
/// </summary>
internal static class MacNativeReplayAudioMuxer
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
        if (wavPaths == null || wavPaths.Count == 0)
            throw new ArgumentException("At least one WAV path is required.", nameof(wavPaths));
        if (string.IsNullOrWhiteSpace(finalPath))
            throw new ArgumentException("A final video path is required.", nameof(finalPath));

        var nativePaths = new IntPtr[wavPaths.Count];
        var nativePathArray = IntPtr.Zero;
        try
        {
            for (var index = 0; index < wavPaths.Count; index++)
                nativePaths[index] = AllocateUtf8(wavPaths[index]);

            nativePathArray = Marshal.AllocHGlobal(checked(IntPtr.Size * nativePaths.Length));
            for (var index = 0; index < nativePaths.Length; index++)
                Marshal.WriteIntPtr(nativePathArray, index * IntPtr.Size, nativePaths[index]);

            var error = new StringBuilder(2048);
            var timeoutMs = (int)Math.Max(1, Math.Min(int.MaxValue, timeout.TotalMilliseconds));
            var result = BppVtMuxAudio(
                silentVideoPath,
                nativePathArray,
                nativePaths.Length,
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
            foreach (var nativePath in nativePaths)
            {
                if (nativePath != IntPtr.Zero)
                    Marshal.FreeHGlobal(nativePath);
            }
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
        MacMetalVideoEncoder.NativeLibrary,
        CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi
    )]
    private static extern int BppVtMuxAudio(
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
