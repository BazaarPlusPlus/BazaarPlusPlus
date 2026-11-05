using System;
using System.IO;
using System.Text;
using SteamKit2;

namespace BazaarPlusPlus.Ios.Service
{
    // iOS does not expose the desktop IOKit/DiskArbitration identifiers used by SteamKit.
    // A random installation ID gives Steam a stable client identity without hardware access.
    public sealed class InstallationIdentity : IMachineInfoProvider
    {
        readonly string id;
        public InstallationIdentity(string root)
        {
            string path = Path.Combine(root, "installation-id");
            if (!File.Exists(path)) File.WriteAllText(path, Guid.NewGuid().ToString("N"));
            id = File.ReadAllText(path).Trim();
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid installation ID");
        }
        public uint LoginId => LoginIdFor(id);
        /// Steam keeps one session per login ID on an account: with one fixed ID, each device that signs in logs out the others.
        public static uint LoginIdFor(string installation)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToUInt32(sha.ComputeHash(Encoding.UTF8.GetBytes(installation)), 0) | 1;
        }
        public byte[] GetMachineGuid() => Encoding.UTF8.GetBytes(id);
        public byte[] GetMacAddress() => Encoding.UTF8.GetBytes(id + ":network");
        public byte[] GetDiskId() => Encoding.UTF8.GetBytes(id + ":storage");
    }
}
