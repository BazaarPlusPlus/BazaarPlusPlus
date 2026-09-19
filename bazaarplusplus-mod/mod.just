set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Some VPN/tunnel configurations advertise an unusable IPv6 route; see scripts/lib.sh.
export DOTNET_SYSTEM_NET_DISABLEIPV6 := env('DOTNET_SYSTEM_NET_DISABLEIPV6', '1')

# The four published assemblies; each keeps a committed NuGet lock file.
published_projects := "src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj src/BazaarPlusPlus/BazaarPlusPlus.csproj"

# Check formatting, compile without deploying, and validate the NuGet lock files.
check: fmt-check build locks-check

# Compile without changing the installed game; [--fast] [-p:Name=Value ...].
build *args:
    bash scripts/build.sh build "$@"

# Compile, repair the macOS trampoline, and copy into the installed game; [--fast] [-p:Name=Value ...].
deploy *args:
    bash scripts/build.sh deploy "$@"

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

# Decompile game DLLs (default Assembly-CSharp, or `all`) into decompiled/ or decompiled-vptr/.
[arg('channel', pattern='online|ptr')]
decompile channel *dlls:
    bash scripts/game.sh decompile "$@"

# Archive the installed Managed dir under game-libs/ for release pinning and matrix builds.
snapshot:
    bash scripts/game.sh snapshot

# Compile against every archived Managed snapshot under game-libs/.
matrix:
    bash scripts/build.sh matrix
