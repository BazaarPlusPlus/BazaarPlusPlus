set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Build the App for the simulator or a device; the game must be at build/steam/download/TheBazaar.app.
build target *args:
    python3 tools/build.py "$@"

# Install the simulator build and open its test channel.
run:
    python3 tools/run-app.py

# Sign in from the saved login, download, launch, and check the game starts (simulator, --test-control build).
regress:
    bash tools/regress.sh

# Check the device build's native dependencies.
audit-device:
    python3 tools/audit-device.py

# Check the Mono patch series and compile the build scripts.
check:
    python3 runtime/mono/bootstrap.py --check
    python3 -m py_compile tools/*.py runtime/mono/*.py runtime/engine/*.py middleware/*/*.py service/*.py

# Run the Python and service unit tests.
test:
    python3 -m unittest discover -s tests
    dotnet run --project service/Tests -c Release

# Zip the signed device build into a sideloadable IPA under build/.
ipa:
    python3 tools/package-ipa.py
