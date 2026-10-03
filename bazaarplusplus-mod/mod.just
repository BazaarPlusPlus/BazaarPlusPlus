set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Some VPN/tunnel configurations advertise an unusable IPv6 route; see scripts/lib.sh.
export DOTNET_SYSTEM_NET_DISABLEIPV6 := env('DOTNET_SYSTEM_NET_DISABLEIPV6', '1')

# The four published assemblies; each keeps a committed NuGet lock file.
published_projects := "src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj src/BazaarPlusPlus/BazaarPlusPlus.csproj"

# Check formatting, compile without deploying, and validate the NuGet and snapshot lock files.
check: fmt-check build locks-check lock-check

# Compile; --deploy also repairs the macOS trampoline and copies into the installed game; [--deploy] [--fast] [-p:Name=Value ...].
build *args:
    bash scripts/build.sh build "$@"

# Run the offline default xUnit suite; [-p:Name=Value ...].
test *args:
    bash scripts/test.sh test "$@"

# Run every compatibility check whose local game or decompiled inputs exist.
test-compat *args:
    bash scripts/test.sh test-compat "$@"

# Run the opt-in Combat Impact acceptance: <absolute-corpus-path> [report-path] [--benchmark].
test-corpus +args:
    bash scripts/test.sh test-corpus "$@"

# Format C# files with the repo-pinned CSharpier.
fmt:
    dotnet tool restore
    dotnet csharpier format .

# Fail on C# files that the repo-pinned CSharpier would reformat.
fmt-check:
    dotnet tool restore
    dotnet csharpier check .

# Rewrite the committed NuGet lock files; review every changed packages.lock.json.
locks:
    for project in {{ published_projects }}; do dotnet restore "$project" --force-evaluate; done

# Validate the committed NuGet lock files without rewriting them.
locks-check:
    for project in {{ published_projects }}; do dotnet restore "$project" --locked-mode; done

# Refresh the remote embedded seeds without building; [-p:Name=Value ...].
fetch-data *args:
    bash scripts/build.sh fetch-data "$@"

# Decompile game DLLs (default Assembly-CSharp, or `all`) into decompiled/, decompiled-vstaging/ or decompiled-vptr/.
[arg('channel', pattern='online|staging|ptr')]
decompile channel *dlls:
    bash scripts/game.sh decompile "$@"

# Capture the mounted game's Managed dir under game-libs/ and record it in build/game-libs.lock.json.
snapshot:
    bash scripts/game.sh snapshot

# Upload a captured snapshot to the private store; needs the release credentials.
[arg('channel', pattern='online|staging|ptr')]
[arg('platform', pattern='macos|windows')]
publish platform channel:
    node ../scripts/workspace.mjs run release -- bash scripts/game.sh publish "$@"

# Make one lock entry's Managed dir available (snapshot, matching Steam install, or private store) and print it.
[arg('channel', pattern='online|staging|ptr')]
[arg('platform', pattern='macos|windows')]
fetch platform channel:
    bash scripts/game.sh fetch "$@"

# Validate build/game-libs.lock.json; empty or stale entries warn, a mismatched Managed dir fails.
lock-check:
    bash scripts/game.sh lock-check

# Compile against every captured lock entry.
matrix:
    bash scripts/build.sh matrix
