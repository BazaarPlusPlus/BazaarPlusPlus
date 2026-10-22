# Android Host

`BazaarPlusPlus.Android` (`net10.0`) is the Android Host (root `CONTEXT.md`, Host). It is not in the Payload yet. It is a MelonMod (`src/BazaarPlusPlus.Android/Host/AndroidMod.cs`) compiled against the Interop Snapshot and the Loader Release (`build/InteropPath.props`), never against `Managed/`.

## Shape

- `Host/` owns the loader entry, the log exit (`AndroidLog`), the settings store over `MelonLoader/UserData/BazaarPlusPlus.cfg` (`IniSettingsStore`) and the Data Root (`AndroidPaths`): `BazaarPlusPlusV6` beside the MelonLoader base directory. Nothing writes there yet.
- `Patches/` install per class under the `[Android] PatchMode` setting (`enabled`, `observe`, `disabled`). A class that fails to install degrades only itself.
- The host does not reference `BazaarPlusPlus.Core` yet, because Core references Storage, which binds the desktop game assemblies. Until then it links the Core and desktop sources it needs; `BazaarPlusPlus.Android.csproj` lists them.

## Build and test

`tests/AndroidHost.Tests` fails if the compiled host references BepInEx or a desktop assembly. It runs in `just mod::test-core`, the suites that need no Managed snapshot, and in the `mod-android` CI lane. `just mod::android-*` drives one device.

## Gotchas

- The startup `build_channel.probe` logs `server_option=missing` on every online build, the Online answer: as on desktop, only the PTR fork of `Config` declares `ServerOption`. It does not show whether Il2CppInterop exposes the nested type; only a PTR or staging Android capture can.
