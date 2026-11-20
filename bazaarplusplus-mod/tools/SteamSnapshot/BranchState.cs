using SteamKit2;

namespace BazaarPlusPlus.SteamSnapshot;

// Steam reports the last change as Unix seconds, with zero when the timestamp is absent.
public sealed record BranchState(
    string Buildid,
    long UpdatedAt,
    IReadOnlyDictionary<uint, ulong> Manifests
)
{
    // Steam uses the same depots shape for app product info and unlocked private branches.
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
        return new BranchState(
            buildid,
            depotSection["branches"][branch]["timeupdated"].AsLong(),
            manifests
        );
    }
}
