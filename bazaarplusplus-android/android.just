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

# Copy build/interop.zip into the debug App's private files as a Development interop: BPP launch then skips the manifest.
push-interop:
    adb push build/interop.zip /data/local/tmp/bpp-interop.zip
    adb shell run-as com.bazaarplusplus.android.dev sh -c "'mkdir -p files/dev && cp /data/local/tmp/bpp-interop.zip files/dev/interop.zip'"
    adb shell rm /data/local/tmp/bpp-interop.zip

# Make a throwaway key and certificate in build/dev-signing/ and have the debug App trust the certificate for interop statements.
dev-signing-key:
    mkdir -p build/dev-signing
    [ -f build/dev-signing/key.pem ] || openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=BazaarPlusPlus development" -keyout build/dev-signing/key.pem -out build/dev-signing/cert.pem
    openssl x509 -in build/dev-signing/cert.pem -outform DER -out build/dev-signing/cert.der
    adb push build/dev-signing/cert.der /data/local/tmp/bpp-trusted-cert.der
    adb shell run-as com.bazaarplusplus.android.dev sh -c "'mkdir -p files/dev && cp /data/local/tmp/bpp-trusted-cert.der files/dev/trusted-cert.der'"
    adb shell rm /data/local/tmp/bpp-trusted-cert.der

# Print the development key's base64 signature of the interop statement for <zip>; <purpose> is "release <version>" or "pre-adaptation".
dev-sign-interop zip il2cpp metadata purpose:
    printf 'bpp-interop-v1\n%s\n%s\n%s\n%s\n' "$(shasum -a 256 "$1" | cut -d ' ' -f 1)" "$2" "$3" "$4" | openssl dgst -sha256 -sign build/dev-signing/key.pem | base64 | tr -d '\n'
    echo

# Have the debug App read releases from http://127.0.0.1:<port> through adb reverse; serve the public installer/ layout there, from latest/mod/android-aarch64.json down.
dev-manifest-port port:
    adb reverse "tcp:$1" "tcp:$1"
    adb shell run-as com.bazaarplusplus.android.dev sh -c "'mkdir -p files/dev && echo $1 > files/dev/manifest-port'"

# Remove every development override (manifest port, Development interop, trusted certificate, game package) from the debug App.
dev-reset:
    adb shell run-as com.bazaarplusplus.android.dev rm -rf files/dev
