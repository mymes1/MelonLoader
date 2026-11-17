namespace MelonPatcher.Core;

public sealed record PatchOptions(
    string InputApk,
    string OutputApk,
    string PayloadDirectory,
    string UnityManagedDirectory,
    string JavaExecutable,
    string DexPatcherJar,
    string BuildToolsDirectory,
    string Keystore,
    string KeyAlias,
    string KeystorePassword,
    string? KeyPassword);
