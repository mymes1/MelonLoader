# Windows APK patcher — experimental legacy MelonLoader 0.4.1

This is a Windows x64 GUI that patches a **standalone Unity IL2CPP arm64-v8a
APK**, aligns it, signs it with your key, verifies it, and exports a new APK.
It uses this repository's **legacy 0.4.1 Android port, not 0.7.3**. The 0.7.3
Android port is incomplete and its artifacts cannot be used as a payload here.
A successful signature check is not proof that the loader or game will run.

## Download the executable

GitHub Actions → **Build Windows APK patcher** → select a successful run →
download `MelonLoader-Legacy-APK-Patcher-Windows-x64`. Extract the ZIP and run
`MelonLoader.LegacyApkPatcher.exe` on 64-bit Windows. The executable includes the
.NET runtime. It does **not** include a loader payload, Java, Android SDK tools,
DexPatcher, a keystore, or Unity files. The workflow tests packaging with mocks and also patches, aligns and signs a
synthetic APK using real tools; it does not produce a game APK or test on a device.

## Required inputs

1. Your original standalone `.apk`. The patcher expects `classes.dex`,
   `lib/arm64-v8a/libunity.so` and `lib/arm64-v8a/libil2cpp.so`; it rejects
   APKs containing other native ABIs, split APKs are not supported, and the
   game must use a `UnityPlayerActivity` compatible with the legacy dex patch.
   Merely passing these checks does not prove the game is compatible.
2. A **complete compatible 0.4.1 legacy payload directory** in the layout below.
   Build/stage it using the legacy [Android instructions](../AndroidInstaller/Readme.md).
   The Actions legacy managed artifact alone is incomplete: Mono, native
   bootstrap, dex bindings, bHaptics and other inputs must be obtained/built
   separately. The app rejects a different MelonLoader assembly version, missing
   required files, non-arm64 native ELF headers, and a payload attempting to
   replace the game's `libunity.so` or `libil2cpp.so`. Those checks do not
   establish runtime compatibility or provenance.
3. Managed Unity IL2CPP DLLs from the Android module of the **exact matching
   Unity Editor version**, usually
   `Editor/Data/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Managed`.
4. A Java 17 JDK (`java.exe`), a compatible [DexPatcher tool JAR](https://github.com/DexPatcher/dexpatcher-tool/releases),
   and Android SDK **Build Tools 35.0.0 or newer** folder containing
   `zipalign.exe` and `lib/apksigner.jar`. Build Tools 35 provides the `-P 16`
   page-alignment option. Tools are supplied by you and are not downloaded by
   the application.
5. A keystore and key alias you control. The patcher takes the passwords in the
   GUI and passes them to the signing subprocess through environment variables,
   not command-line arguments or saved config. A process with sufficient local
   privileges may still inspect environment variables.

Payload layout (other DLLs and arm64 Mono `.so` dependencies may be needed):

```text
dependencies/
  dex/classes.dex                    # compiled legacy JavaBindings patch dex
  core/MelonLoader.dll               # version 0.4.1
  mono/bcl/*.dll                     # includes mscorlib.dll
  support_modules/Il2Cpp.dll
  assembly_generation/*.dll          # generator, Unhollower, Iced, etc.
  native/*.so                        # arm64 Bootstrap, funchook, capstone, Mono, bHaptics
```

## Patch an APK

1. Back up the APK and game data. Only modify software you are authorized to.
2. Fill every path, choose a **new** output filename, enter the alias and keystore
   password (plus the key password if it differs), then click **Patch, align,
   sign and export**. The original is never overwritten. The patcher removes old
   APK signature files, patches dex with DexPatcher, adds the legacy files,
   aligns stored libraries to 16 KB, signs with `apksigner`, verifies the
   signature and alignment, then writes the result. Temporary files are deleted
   on normal completion or error. Abrupt termination can leave temporary files.
3. Install the exported APK on a test device only after handling signing identity:
   a differently signed APK cannot update the store-signed app, uninstalling the
   original may remove saved data, and store/signature checks or anti-cheat may
   reject it. Do not bypass these checks. `adb logcat -s MelonLoader` can help
   diagnose initialization failures.

The legacy loader uses the app's external files directory, normally
`/sdcard/Android/data/<package>/files`; its `Mods` folder is created if the
loader starts. Scoped-storage restrictions can make this directory difficult to
access. Modern Android versions, Unity versions and 16 KB-page devices require
compatible native binaries; zip alignment alone does not make older `.so` files
16 KB compatible. No game/device has been verified with this patcher.
