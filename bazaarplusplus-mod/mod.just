set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Some VPN/tunnel configurations advertise an unusable IPv6 route; see scripts/mod-env.ts.
export DOTNET_SYSTEM_NET_DISABLEIPV6 := env('DOTNET_SYSTEM_NET_DISABLEIPV6', '1')

# The published assemblies; each keeps a committed NuGet lock file.
published_projects := "src/BazaarPlusPlus.Core/BazaarPlusPlus.Core.csproj src/BazaarPlusPlus.Updater/BazaarPlusPlus.Updater.csproj src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj src/BazaarPlusPlus/BazaarPlusPlus.csproj"
# Runs with the Steam login in its environment, so it keeps a lock file too.
steam_tool := "tools/SteamSnapshot/SteamSnapshot.csproj"
# Development only; it holds the bundle_sync token, so its package graph stays locked too.
sampler_tool := "tools/HistorySampler/HistorySampler.csproj"

# Check formatting, compile without deploying, and validate the NuGet and snapshot lock files.
check: fmt-check build locks-check lock-check

# Compile; --deploy also repairs the macOS trampoline and copies into the installed game; [--deploy] [--fast] [-p:Name=Value ...].
build *args:
    bun scripts/build.ts build "$@"

# Run the offline default xUnit suite; [-p:Name=Value ...].
test *args:
    bun scripts/test.ts test "$@"

# Run timing assertions separately, after the functional suites have finished.
test-performance *args:
    bun scripts/test.ts test-performance "$@"

# Run the suites that need no Managed snapshot (Linux too; needs the android publisher fetch); [-p:Name=Value ...].
test-core *args:
    bun scripts/test.ts test-core "$@"

# Run Android build and functional/performance gates serially; save logs and a revision receipt.
android-check:
    bun ../scripts/mod-validation.ts check

# Compile the Android host against the fetched Interop Snapshot; [-p:Name=Value ...].
android-build *args:
    bun ../scripts/android/mod-loop.ts build "$@"

# Push the Android build to its MelonLoader Payload paths on ANDROID_SERIAL, or the only connected device.
android-deploy:
    bun ../scripts/android/mod-loop.ts deploy

# Set the Android host's patch mode on the device; it applies on the next launch.
[arg('mode', pattern='enabled|observe|disabled')]
android-mode mode:
    bun ../scripts/android/mod-loop.ts mode "$@"

# Set one key of the device's BazaarPlusPlus.cfg, such as Touch UpgradePreview true; it applies on the next launch.
android-setting section key value:
    bun ../scripts/android/mod-loop.ts setting "$@"

# Restart the debug Android App with an empty logcat buffer and tap Play on its launcher.
android-launch:
    bun ../scripts/android/mod-loop.ts launch

# Restart the debug Android App and stop at its launcher, without starting the game.
android-launcher:
    bun ../scripts/android/mod-loop.ts launcher

# Development only: open panels in the running desktop game and capture each inside the game; see --help.
ui-shots *args:
    bun ../scripts/dev/desktop-shots.ts "$@"

# Save logs, a screenshot and display metadata under artifacts/android/<run>/.
android-capture run:
    bun ../scripts/android/mod-loop.ts capture "$@"

# Capture and reject portrait or overridden displays before judging UI layout; never changes device settings.
android-screen-check run:
    bun ../scripts/android/mod-loop.ts screen-check "$@"

# Build, deploy, relaunch in a mode, capture into artifacts/android/smoke-<mode>/, and assert the three levels.
[arg('mode', pattern='enabled|observe|disabled')]
android-smoke mode:
    bun ../scripts/android/mod-loop.ts smoke "$@"

# Re-assert the three smoke levels on a capture directory, such as a downloaded CI artifact.
android-verify dir:
    bun ../scripts/android/mod-loop.ts verify "$@"

# Export a game replay corpus to a Combat Impact Corpus and project it: <absolute-corpus-path> [report-path] [--benchmark].
test-corpus +args:
    bun scripts/test.ts test-corpus "$@"

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
    for project in {{ published_projects }} {{ steam_tool }} {{ sampler_tool }}; do dotnet restore "$project" --force-evaluate; done

# Validate the committed NuGet lock files without rewriting them.
locks-check:
    for project in {{ published_projects }} {{ steam_tool }} {{ sampler_tool }}; do dotnet restore "$project" --locked-mode; done

# Refresh the remote embedded seeds without building; [-p:Name=Value ...].
fetch-data *args:
    bun scripts/build.ts fetch-data "$@"

# Decompile game DLLs (default Assembly-CSharp, or `all`) into decompiled/, decompiled-vstaging/ or decompiled-vptr/.
[arg('channel', pattern='online|staging|ptr')]
decompile channel *dlls:
    bun scripts/game.ts decompile "$@"

# Capture the mounted game's Managed dir under game-libs/ and record it in build/game-libs.lock.json.
snapshot:
    bun scripts/game.ts snapshot

# Capture an official Android build's Interop Snapshot (base APK, then any splits) under game-libs/ and record it in build/game-libs.lock.json.
snapshot-android apk *args:
    bun scripts/game.ts snapshot-android "$@"

# Sign in to Steam at the terminal and save the login the Steam capture recipes use; the operator runs this.
steam-login:
    bun scripts/game.ts steam-login

# Print the Steam buildid of a channel and the platforms whose lock entry is behind it, as JSON.
[arg('channel', pattern='online|staging|ptr')]
steam-stale channel:
    bun scripts/game.ts steam-stale "$@"

# Download one lock entry's Managed dir from its Steam depot and record it in build/game-libs.lock.json.
[arg('channel', pattern='online|staging|ptr')]
[arg('platform', pattern='macos|windows')]
steam-capture platform channel:
    bun scripts/game.ts steam-capture "$@"

# Upload a captured snapshot (android: and its pinned inputs) to the private store; needs the release credentials.
[arg('channel', pattern='online|staging|ptr|publisher|play')]
[arg('platform', pattern='macos|windows|android')]
publish platform channel:
    bun ../scripts/workspace.ts run release -- bun scripts/game.ts publish "$@"

# Make one lock entry's Managed dir (android: interop dir and Loader Release) available and print it.
[arg('channel', pattern='online|staging|ptr|publisher|play')]
[arg('platform', pattern='macos|windows|android')]
fetch platform channel:
    bun scripts/game.ts fetch "$@"

# Validate build/game-libs.lock.json; empty or stale entries warn, a mismatched Managed dir fails.
lock-check:
    bun scripts/game.ts lock-check

# CompatCheck-compile against every captured lock entry, or against the host platform's named channels (an empty entry fails).
matrix *channels:
    bun scripts/build.ts matrix "$@"
