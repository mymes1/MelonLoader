# Experimental MelonLoader 0.7.3 Android port — not an installable release

These are experimental components, separate from the legacy 0.4.1 Android
port. They have **not been run together on a device**. Successful builds,
interop generation, ELF inspection and APK signature checks do not establish
that the runtime, Unity lifecycle hook, bootstrap, mods, or a game will work.
Do not use the legacy Windows patcher or installer with these files.

## Source and licensing

`upstream/` starts with [LavaGang/MelonLoader v0.7.3](https://github.com/LavaGang/MelonLoader/tree/v0.7.3),
commit `982ed9981ac5b0619648a2e79bcc02fd8861db33`, modified for Android.
See `upstream/LICENSE.md` and `upstream/NOTICE.txt`. `BionicNativeAot.targets`,
`androidexports.def` and the arm64 Dobby/plthook static archives originate in
[LemonLoader's 0.7.0 branch](https://github.com/LemonLoader/MelonLoader/tree/7b14dac3281fe9a88a1b8f0c96f5b9a46f558cdf).
The Dobby and plthook archives are prebuilt and have not been validated for
this port or for devices with 16 KB pages.

The reference Android .NET 8.0.6 runtime in Actions is extracted from
[LemonLoader 0.6.5.1 melon_data.zip](https://github.com/LemonLoader/MelonLoader/releases/tag/0.6.5.1),
SHA-256 `1c8bab9b0bf7a462f5efe03e8dc253f0f520357915641669d0910f6a9800f3a6`.
The workflow checks its hash and arm64 ELF headers, not its compatibility with
0.7.3. The runtime artifact includes the .NET license and provenance. The
reference runtime is an external binary, **not built from source here**.

## Actions artifacts

A successful **Build experimental 0.7.3 Android components** run publishes:

- `melonloader-073-android-managed-experimental`: 0.7.3 managed components,
  built for net6 with major-version roll-forward configured for .NET 8.
- `melonloader-073-android-arm64-bootstrap-experimental`: ARM64 NativeAOT
  bootstrap, built with the Android NDK, checked for its entry point and a
  glibc dependency. NativeAOT trimming warnings still require runtime testing.
- `melonloader-073-android-dotnet8-reference-experimental`: the reference
  runtime described above, with `dotnet/host/fxr/8.0.6/libhostfxr.so` and
  `dotnet/shared/Microsoft.NETCore.App/8.0.6/libcoreclr.so`.
- `melonloader-073-android-arm64-bridge-experimental`: JNI bridge and patch dex.
  The `UnityPlayerActivity.onCreate` prepend is tested using real DexPatcher on
  a synthetic class. Its preload/hook ordering has not been verified against a
  real Unity player; custom activities and split APKs are not supported.

The **Test experimental 0.7.3 offline interop generation** workflow builds and
publishes `melonloader-073-offline-interop-generator-windows-experimental`.
It runs the real Cpp2IL and Il2CppInterop generator against a public Unity test
fixture, but that fixture is a **Windows** game binary, not an Android ELF.
The workflow does not establish Android support, arbitrary Unity version
compatibility, or correctness of generated proxies on a device.

## Generate interop assemblies offline

The Android loader cannot execute desktop Cpp2IL. Instead the experimental
`InteropGen` tool runs Cpp2IL and Il2CppInterop on a desktop and creates proxy
DLLs with a manifest binding them to the exact game binary and metadata. The
Android loader verifies these hashes before using the generated assemblies;
this detects accidental mismatches, **not malicious modification**. Do not use
the Windows game fixture output with an Android APK.

You need the exact game's `lib/arm64-v8a/libil2cpp.so` and
`assets/bin/Data/il2cpp_data/Metadata/global-metadata.dat` (extracted from your
APK), its precise Unity version and matching Unity managed base DLLs, plus
[Cpp2IL 2022.1.0-pre-release.21](https://github.com/SamboyCoding/Cpp2IL/releases/tag/2022.1.0-pre-release.21)
for your desktop. The tool does not download these inputs. Unity libraries may
be subject to separate licensing terms.

Example on Windows (use your actual paths/version; output must not exist):

```powershell
.\InteropGen.exe --binary C:\game\libil2cpp.so `
  --metadata C:\game\global-metadata.dat --unity-version 2019.4.34f1 `
  --unity-libs C:\game\UnityManaged --cpp2il C:\tools\Cpp2IL.exe `
  --output C:\game\interop
```

The manifest ties output to the provided metadata and binary, but not to the
Unity library provenance, so you must supply the matching version. The tool may
not handle obfuscation, stripped code registration, or newer Unity versions.

## Stage components for development

After generating **game-specific** interop, assemble a new staging directory
from the extracted Actions artifacts (paths are examples):

```bash
python3 AndroidPort073/stage.py \
  --managed /path/to/managed/MelonLoader \
  --dotnet /path/to/reference-runtime/dotnet \
  --bootstrap /path/to/libMelonLoader.Bootstrap.so \
  --bridge /path/to/libmelonloader073_bridge.so \
  --dex /path/to/bridge/dex \
  --interop /path/to/generated-interop \
  --output /path/to/new-payload-directory
```

The staged tree contains `assets/MelonLoader` (including the pre-generated
`Il2CppAssemblies`), `assets/dotnet`, `lib/arm64-v8a`, and `dex`. It is **not**
an APK. No current patcher consumes it. The staging utility checks required
files and rejects symlinks; it does not validate native compatibility. The
candidate Java extractor copies `assets/bin/Data` and the game binary into
private storage each initialization, potentially using substantial space.

## Remaining work before installation

1. Test bootstrap, reference CoreCLR and the net6-to-net8 roll-forward together
   on real Android devices. Reconcile runtime/thread/GC behavior with IL2CPP,
   verify NativeAOT dynamic-code/trimming warnings and native symbol/hook use.
2. Test the lifecycle injection order across supported Unity versions and
   Android versions. `libunity.so` must be loaded and the PLT hook installed
   before Unity resolves the IL2CPP entry points; this is not established.
3. Validate generation from actual Android ARM64 games, Unity dependency and
   obfuscation cases, and IL2CPP interop execution. Check 16 KB page support for
   *all* native dependencies, not just APK zip alignment.
4. Build an APK integration path and verify install, signing, execution, mods,
   logging and app updates on physical devices. Re-signing with a different key
   prevents an in-place update and uninstalling may erase saved data. Only modify
   APKs you are authorized to modify. Mods would be in private app storage if
   the loader starts, and may not be accessible to a normal file manager.

Run `python3 -m unittest discover -s tests -v` for host-side staging, JNI stub,
and installer tests. These use fake files and do not test Android runtime.
