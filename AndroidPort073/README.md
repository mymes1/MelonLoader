# Experimental MelonLoader 0.7.3 Android port — incomplete

This directory contains **work in progress, not an installable release**. It is
separate from the 0.4.1-based loader and its `AndroidInstaller` scripts; do not
mix their binaries or APK payloads. Nothing here has been built or run on an
Android device yet. Staging succeeds only with external, prebuilt inputs and is
not proof that they are compatible.

## Source and licensing

`upstream/` starts with [LavaGang/MelonLoader v0.7.3](https://github.com/LavaGang/MelonLoader/tree/v0.7.3),
commit `982ed9981ac5b0619648a2e79bcc02fd8861db33`, modified for this experiment.
The three desktop .NET runtime archives, `BaseLibs`, and GitHub workflows were
omitted; those desktop assets do not supply an Android runtime. Existing upstream
source and reference DLLs are included to support future porting work. Its Apache
2.0 license and notice are in `upstream/LICENSE.md` and `upstream/NOTICE.txt`.
`BionicNativeAot.targets`, `androidexports.def` and the arm64 Dobby/plthook static
archives come from [LemonLoader's 0.7.0 branch](https://github.com/LemonLoader/MelonLoader/tree/7b14dac3281fe9a88a1b8f0c96f5b9a46f558cdf)
(commit `7b14dac3281fe9a88a1b8f0c96f5b9a46f558cdf`); that fork's credits and
third-party license list are in [its README](https://github.com/LemonLoader/MelonLoader/blob/7b14dac3281fe9a88a1b8f0c96f5b9a46f558cdf/README.md).
Dobby is Apache 2.0 and plthook is BSD 2-Clause. The prebuilt archives have not
been verified for this 0.7.3 build.

## Work done

- The upstream 0.7.3 managed code and NativeAOT bootstrap have their own copy,
  with an Android RID/constant, Android base paths and Android `libdl` bindings.
- `native/bridge.c` and `java/.../Loader073.java` implement an experimental JNI
  entry point. The Java side copies payloads and Unity data into private app
  storage and the native shim asks the 0.7.3 bootstrap to hook the already-loaded
  Unity library. A failed PLT hook is reported rather than treated as success.
- `stage.py` creates an asset/native-library directory tree from **prebuilt**
  managed code, an Android .NET runtime, the bootstrap, and JNI bridge. It checks
  for the expected files, refuses symlinks and existing output, and never patches
  an APK. It does not validate ABI, signatures or runtime compatibility.
- The 0.7.3 desktop Cpp2IL generator path is disabled for Android: it would try
  to download and execute a desktop binary. This means IL2CPP generation is **not
  functional**, even if the bootstrap happens to initialize.

## What must be completed before installing

1. Obtain or build a compatible ARM64 **bionic .NET NativeAOT toolchain/runtime
   pack**, `libnethost`, and an Android .NET runtime. Upstream 0.7.3 expects a
   net9 NativeAOT bootstrap and net6 managed runtime; runtime roll-forward and
   LemonLoader's Android-specific runtime/GC patches need to be reconciled.
   `BionicNativeAot.targets` is a starting point, not a validated build recipe.
2. Build and verify the 0.7.3 managed assemblies, support modules, NativeAOT
   bootstrap, and `native/` bridge with the appropriate SDK, NDK, NuGet feeds,
   and native libraries. The `Build experimental 0.7.3 Android components`
   workflow attempts the managed, JNI bridge, and Java/dex builds; it does not
   build the NativeAOT bootstrap, supply an Android runtime, or produce an APK.
3. Implement Android-compatible IL2CPP assembly generation (including game
   metadata, Cpp2IL execution, and Il2CppInterop), thread/GC integration, Android
   logging and any missing runtime-specific native symbols. `libil2cpp.so` and
   game data extraction here are only an initial adaptation; Unity versions,
   asset layouts and split APKs differ.
4. Build dex bindings and integrate a **verified** Unity lifecycle hook. The
   bridge requires `libunity.so` to be loaded but must attach before Unity looks
   up `il2cpp_init`; the legacy `UnityPlayerActivity.onCreate` hook has **not**
   been shown to meet that requirement. No dex patching step invokes `Loader073`
   yet. Do not use the legacy installer with this payload.
5. Create, align, sign and verify a test APK; test supported Unity versions and
   Android devices, including 16 KB pages, storage restrictions, and app signing.
   Re-signing normally prevents updating the store-signed app and uninstalling
   it can remove saves. Only modify APKs you are authorized to modify.

Once compatible components exist, this is the staging interface (paths shown
are examples, not bundled binaries):

```bash
python3 AndroidPort073/stage.py \
  --managed /path/to/built/MelonLoader \
  --dotnet /path/to/bionic/dotnet \
  --bootstrap /path/to/libMelonLoader.Bootstrap.so \
  --bridge /path/to/libmelonloader073_bridge.so \
  --output /path/to/new-payload-directory
```

The staged tree contains `assets/MelonLoader`, `assets/dotnet` and
`lib/arm64-v8a`; it is **not** a signed or patched APK. The current extractor
copies all `assets/bin/Data` to private storage each initialization, which can
require substantial free space and time. It handles only an arm64 native library
inside the base APK, not split APKs. Mods would be under the app's **private**
`files/melonloader073/Mods` directory if the port eventually starts; standard
file managers may not be able to access it.

## Tests

Run `python3 -m unittest discover -s tests -v`. The 0.7.3 workflow attempts
component builds and publishes separate experimental artifacts; a green run is
not evidence that the incomplete loader works on Android. Staging tests use dummy files;
the JNI shim test compiles against host stubs. These do not build the managed
projects, test the Android NDK, package a real APK, or run on a device.
