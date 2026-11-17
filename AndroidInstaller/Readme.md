# Experimental Android APK patcher (legacy fork)

**Status:** this installer targets an old Android port identifying itself as MelonLoader
**0.4.1**, not 0.7.3. The build workflow produces managed build outputs only; it does
not produce an APK or a complete Android distribution. The original desktop README
is not an Android installation guide. This patcher is experimental and has not been
validated against current Android versions, games, or devices. Back up the APK and
all game data before experimenting; use only APKs you have permission to modify.

## About 0.7.3

[Upstream v0.7.3](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3)
contains desktop releases, not an Android APK or Android loader. This fork's Mono,
bootstrap, assembly generation and Java injection are from a much older loader;
its Unhollower-based IL2CPP integration is not the v0.7.3 Il2CppInterop runtime.
Replacing DLLs or changing the version string does not upgrade it. A real 0.7.3
Android port requires integrating the upstream managed code and native bootstrap,
Android-compatible runtime and dependencies, Java entry point, and IL2CPP tooling,
then building and testing on devices. An [experimental 0.7.3 port](../AndroidPort073/README.md) has been started separately, but is not complete or installable.
For a more recent Android-focused fork, see [LemonLoader](https://github.com/LemonLoader/MelonLoader)
and its [installer](https://github.com/LemonLoader/MelonLoaderInstaller); check
those projects' release notes for actual supported versions rather than assuming 0.7.3.

## Prerequisites for this legacy patcher

- Linux/bash, `zip`, `unzip`, `realpath`, a JDK, Android SDK build tools with
  `d8`, `zipalign` and `apksigner`, and [DexPatcher](https://github.com/DexPatcher/dexpatcher-tool).
- One standalone Unity **IL2CPP arm64-v8a** APK with `UnityPlayerActivity` and
  root `classes*.dex` files. The patcher checks for arm64 `libunity.so` and
  `libil2cpp.so`, but cannot prove the activity/injection is compatible. Split
  APKs/app bundles, Mono games, 32-bit-only devices, and custom Unity activities
  are not supported by this script. A multi-ABI APK must actually run as arm64;
  other ABIs do not receive the loader.
- An appropriate Unity Editor Android IL2CPP module for the game's exact Unity
  version. The script uses its managed Unity assemblies for generation, but
  **preserves the game's own `libunity.so`**.
- Separately built, compatible arm64 Android Mono BCL and native libraries,
  arm64 `libbhaptics-native-lib.so`, and the compiled components below. These
  inputs are not provided by the workflow, and their exact provenance/build
  instructions are not established by this old repository. Do not substitute
  desktop binaries. A build of this repository alone is not a ready-to-use APK.
- A signing keystore you control. Re-signing changes the app's identity: a patched
  APK cannot update the store-signed app without the original signing key. Removing
  the original can delete saves, and signature checks, app stores, anti-cheat or
  DRM can prevent the patched app from running. Do not bypass those protections.

## Build and stage the existing port

1. Clone with submodules: `git submodule update --init --recursive`.
2. On a machine with Visual Studio MSBuild and .NET Framework 4.7.2 targeting
   support, build the legacy managed solution using:
   `msbuild MelonLoader.sln -restore -p:Configuration=Debug -p:Platform=Android-ARM64`.
   The CI workflow runs the same configuration on Windows. It is not an Android
   SDK app build and does not bundle Mono, native libraries or Unity files.
3. On a machine with the Android NDK, copy `Bootstrap/tools/.env.sample` to
   `Bootstrap/tools/.env` and configure `NDK`, `ABI=arm64-v8a`, and
   `MINSDKVERSION=23`. The legacy build expects native outputs in
   `Bootstrap/.cmake/android/debug`. For example, from the repository root:
   ```bash
   mkdir -p Bootstrap/.cmake/android/debug
   Bootstrap/tools/cmake_wrapper.sh -p android -S Bootstrap -B Bootstrap/.cmake/android/debug -DCMAKE_BUILD_TYPE=Debug
   Bootstrap/tools/cmake_wrapper.sh --build Bootstrap/.cmake/android/debug
   ```
4. Configure `JavaBindings/bin/.env` with the SDK build-tools and Android platform
   paths, then run `bash JavaBindings/bin/build.sh`. It uses the pinned bHaptics
   submodule and produces `JavaBindings/build/dex`.
5. Copy `AndroidInstaller/.env.example` to `AndroidInstaller/.env` and edit all
   paths. They must point to the original APK, matching Unity editor, DexPatcher,
   compatible prebuilt Mono/bHaptics inputs, and your keystore. The staging script
   checks for the expected core, support, generator, Unhollower and native outputs;
   if a build places files elsewhere, it needs to be adapted before proceeding.

## Patch, sign, and install

From the repository root, with the configured dependencies in place:

```bash
# Read the password interactively rather than putting it in .env or shell history.
read -rs -p 'Keystore password: ' KEYSTORE_PASSWORD; echo
export KEYSTORE_PASSWORD
# If the key password is different, also read and export KEY_PASSWORD.
bash AndroidInstaller/install.sh
unset KEYSTORE_PASSWORD KEY_PASSWORD
```

The script copies the original APK, patches its dex, adds legacy loader assets and
arm64 native libraries, aligns, signs, and verifies the signature. It writes the
result to `AndroidInstaller/out/<original-apk-name>` and leaves the input untouched.
The script removes and recreates `AndroidInstaller/out` on each run **after**
validating the required inputs; move any previous results elsewhere first. The
packaging tests use mock tools, so signature verification and a real device test
are still necessary. The `zipalign -p` configuration targets 4 KB native library
alignment; newer 16 KB-page devices may require a different, validated packaging
pipeline and compatible native libraries.

For a test device with USB debugging and platform-tools, after backing up data and
handling the original app's different signature, install with:

```bash
adb install "AndroidInstaller/out/<original-apk-name>"
adb logcat -s MelonLoader
```

The loader uses the app's external-files directory (usually
`/sdcard/Android/data/<package-name>/files`) as its base. If it starts successfully,
the legacy loader creates a `Mods` directory there; only use mods compatible with
this old Android build and the particular game. Modern Android may restrict access
to that directory. Check logcat and the game's logs for failures; successful APK
signing alone does not establish runtime compatibility.
