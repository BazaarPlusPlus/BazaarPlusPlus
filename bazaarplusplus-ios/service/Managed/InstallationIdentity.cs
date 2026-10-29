using System;
using System.IO;
using System.Text;
using SteamKit2;

namespace BazaarPlusPlus.Ios.Service
{
    // iOS does not expose the desktop IOKit/DiskArbitration identifiers used by SteamKit.
    // A random installation ID gives Steam a stable client identity without hardware access.
    internal sealed class InstallationIdentity : IMachineInfoProvider
    {
        readonly string id;
        public InstallationIdentity(string root)
        {
            string path = Path.Combine(root, "installation-id");
            if (!File.Exists(path)) File.WriteAllText(path, Guid.NewGuid().ToString("N"));
            id = File.ReadAllText(path).Trim();
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid installation ID");
        }
        public byte[] GetMachineGuid() => Encoding.UTF8.GetBytes(id);
        public byte[] GetMacAddress() => Encoding.UTF8.GetBytes(id + ":network");
        public byte[] GetDiskId() => Encoding.UTF8.GetBytes(id + ":storage");
    }
}
