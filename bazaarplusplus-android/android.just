set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Lint the App and build the debug and release APKs; a build without a staged runtime launches the game without BPP.
check:
    ./gradlew --no-daemon lint assembleDebug assembleRelease

# Run the App's JVM unit tests.
test:
    ./gradlew --no-daemon testDebugUnitTest

# Stage the Android App Runtime under build/runtime/ from the locked Loader Release (fetches from the game-libs store).
stage-runtime:
    node ../release/android-app.mjs stage-runtime

# Write the Interop Package for the locked game version to build/interop.zip (fetches from the game-libs store).
interop-package:
    node ../release/android-app.mjs interop-package

# Build and install the debug App on the device that adb selects (ANDROID_SERIAL picks one of several).
install:
    ./gradlew --no-daemon installDebug

# Copy build/interop.zip into the debug App's private files, where it stands in for a downloaded Interop Package.
push-interop:
    adb push build/interop.zip /data/local/tmp/bpp-interop.zip
    adb shell run-as com.bazaarplusplus.android.dev sh -c "'mkdir -p files/dev && cp /data/local/tmp/bpp-interop.zip files/dev/interop.zip'"
    adb shell rm /data/local/tmp/bpp-interop.zip
