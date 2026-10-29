using System;
using System.Runtime.InteropServices;
namespace BazaarPlusPlus.Ios.Service
{
    internal static class Native
    {
        [DllImport("__Internal")] internal static extern void bpp_steam_identity(uint app, ulong steamId);
        [DllImport("__Internal")] internal static extern void bpp_steam_ticket(uint app, uint handle, byte[] ticket, int length, int result);
        [DllImport("__Internal")] internal static extern int bpp_steam_save_session(string json);
        [DllImport("__Internal")] internal static extern IntPtr bpp_steam_load_session();
        [DllImport("__Internal")] internal static extern void bpp_steam_free(IntPtr value);
        [DllImport("__Internal")] internal static extern int bpp_steam_decrypt(byte[] input, int count, byte[] key, byte[] output);
        [DllImport("__Internal")] internal static extern int bpp_steam_file_sha1(string path, [Out] byte[] digest);
        [DllImport("__Internal")] internal static extern int bpp_steam_zstd(byte[] input, int count, byte[] output, int capacity);
        [DllImport("__Internal")] internal static extern int bpp_steam_symlink(string target, string path);
        public static string LoadSession()
        {
            var pointer = bpp_steam_load_session();
            try { return pointer == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(pointer)!; }
            finally { if (pointer != IntPtr.Zero) bpp_steam_free(pointer); }
        }
    }
}
