using SteamKit2;

namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>What Steam reports for one branch: its build and each depot's manifest.</summary>
public sealed record BranchState(string Buildid, IReadOnlyDictionary<uint, ulong> Manifests)
{
    /// <summary>
    /// Reads the branch from a depots section: the app's own, or the private one an access code
    /// unlocks, which has the same shape. A missing value throws.
    /// </summary>
    public static BranchState Read(KeyValue depotSection, string branch, IEnumerable<uint> depots)
    {
        var buildid = depotSection["branches"][branch]["buildid"].AsString();
        if (string.IsNullOrEmpty(buildid))
            throw new InvalidDataException(
                $"Steam shows no build for branch {branch}; the account may not see this branch"
            );
        var manifests = new Dictionary<uint, ulong>();
        foreach (var depot in depots)
        {
            var manifest = depotSection[depot.ToString()]
                ["manifests"][branch]["gid"]
                .AsUnsignedLong();
            if (manifest == 0)
                throw new InvalidDataException(
                    $"Steam shows no manifest for depot {depot} on branch {branch}"
                );
            manifests[depot] = manifest;
        }
        return new BranchState(buildid, manifests);
    }
}
