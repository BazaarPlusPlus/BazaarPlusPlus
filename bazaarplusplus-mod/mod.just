set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Some VPN/tunnel configurations advertise an unusable IPv6 route; see scripts/lib.sh.
export DOTNET_SYSTEM_NET_DISABLEIPV6 := env('DOTNET_SYSTEM_NET_DISABLEIPV6', '1')

# The published assemblies; each keeps a committed NuGet lock file.
published_projects := "src/BazaarPlusPlus.Core/BazaarPlusPlus.Core.csproj src/BazaarPlusPlus.Updater/BazaarPlusPlus.Updater.csproj src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj src/BazaarPlusPlus/BazaarPlusPlus.csproj"

# Check formatting, compile without deploying, and validate the NuGet and snapshot lock files.
check: fmt-check build locks-check lock-check

# Compile; --deploy also repairs the macOS trampoline and copies into the installed game; [--deploy] [--fast] [-p:Name=Value ...].
build *args:
    bash scripts/build.sh build "$@"

# Run the offline default xUnit suite; [-p:Name=Value ...].
test *args:
    bash scripts/test.sh test "$@"

# Run timing assertions separately, after the functional suites have finished.
test-performance *args:
    bash scripts/test.sh test-performance "$@"

# Run the suites that need no Managed snapshot (Linux too; needs the android online fetch); [-p:Name=Value ...].
test-core *args:
    bash scripts/test.sh test-core "$@"

# Run Android build and functional/performance gates serially; save logs and a revision receipt.
android-check:
    node ../scripts/mod-validation.mjs check

# Compile the Android host against the fetched Interop Snapshot; [-p:Name=Value ...].
android-build *args:
    bash scripts/android.sh build "$@"

# Push the Android build to its MelonLoader Payload paths on $BPP_ANDROID_SERIAL (default emulator-5556).
android-deploy:
    bash scripts/android.sh deploy

# Set the Android host's patch mode on the device; it applies on the next launch.
[arg('mode', pattern='enabled|observe|disabled')]
android-mode mode:
    bash scripts/android.sh mode "$@"

# Set one key of the device's BazaarPlusPlus.cfg, such as Android UpgradePreview true; it applies on the next launch.
android-setting section key value:
    bash scripts/android.sh setting "$@"

# Restart the game on the device with an empty logcat buffer.
android-launch:
    bash scripts/android.sh launch

# Save logs, a screenshot and display metadata under artifacts/android/<run>/.
android-capture run:
    bash scripts/android.sh capture "$@"

# Capture and reject portrait or overridden displays before judging UI layout; never changes device settings.
android-screen-check run:
    bash scripts/android.sh screen-check "$@"

# Build, deploy, relaunch in a mode, capture into artifacts/android/smoke-<mode>/, and assert the three levels.
[arg('mode', pattern='enabled|observe|disabled')]
android-smoke mode:
    bash scripts/android.sh smoke "$@"

# Re-assert the three smoke levels on a capture directory, such as a downloaded CI artifact.
android-verify dir:
    bash scripts/android.sh verify "$@"

# Export a game replay corpus to a Combat Impact Corpus and project it: <absolute-corpus-path> [report-path] [--benchmark].
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

# Capture an official Android APK's Interop Snapshot under game-libs/ and record it in build/game-libs.lock.json.
snapshot-android apk *args:
    bash scripts/game.sh snapshot-android "$@"

# Upload a captured snapshot (android: and its pinned inputs) to the private store; needs the release credentials.
[arg('channel', pattern='online|staging|ptr')]
[arg('platform', pattern='macos|windows|android')]
publish platform channel:
    node ../scripts/workspace.mjs run release -- bash scripts/game.sh publish "$@"

# Make one lock entry's Managed dir (android: interop dir and Loader Release) available and print it.
[arg('channel', pattern='online|staging|ptr')]
[arg('platform', pattern='macos|windows|android')]
fetch platform channel:
    bash scripts/game.sh fetch "$@"

# Validate build/game-libs.lock.json; empty or stale entries warn, a mismatched Managed dir fails.
lock-check:
    bash scripts/game.sh lock-check

# CompatCheck-compile against every captured lock entry, or against the host platform's named channels (an empty entry fails).
matrix *channels:
    bash scripts/build.sh matrix "$@"
