using System.IO.Compression;
using MelonPatcher.Core;

// Uses real DexPatcher, zipalign and apksigner on a synthetic APK. Fake native
// libraries only validate packaging; this is not a loader/device compatibility test.
internal static class RealIntegration
{
    internal static async Task RunAsync(string fixture)
    {
        string root = Path.GetFullPath(fixture);
        string payload = Path.Combine(root, "payload"), unity = Path.Combine(root, "unity");
        void Write(string relative, byte[] bytes)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        byte[] dummy = [1, 2, 3];
        byte[] elf = new byte[20];
        elf[0] = 0x7f; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
        elf[4] = 2; elf[5] = 1; elf[18] = 0xb7;
        Write("payload/mono/bcl/mscorlib.dll", dummy);
        Write("payload/support_modules/Il2Cpp.dll", dummy);
        foreach (string name in new[] { "Il2CppAssemblyGenerator.dll", "AssemblyUnhollower.dll", "UnhollowerBaseLib.dll", "UnhollowerRuntimeLib.dll", "Iced.dll" })
            Write("payload/assembly_generation/" + name, dummy);
        foreach (string name in new[] { "libBootstrap.so", "libfunchook.so", "libcapstone.so", "libbhaptics-native-lib.so", "libmono.so" })
            Write("payload/native/" + name, elf);
        Write("unity/UnityEngine.dll", dummy);
        Directory.CreateDirectory(Path.Combine(payload, "core"));
        File.Copy(typeof(DummyLegacy.Marker).Assembly.Location, Path.Combine(payload, "core", "MelonLoader.dll"));
        Directory.CreateDirectory(Path.Combine(payload, "dex"));
        File.Copy(Path.Combine(root, "patchdex", "classes.dex"), Path.Combine(payload, "dex", "classes.dex"));
        string original = Path.Combine(root, "original.apk"), exported = Path.Combine(root, "patched.apk");
        File.Copy(Path.Combine(root, "base.apk"), original);
        using (var apk = ZipFile.Open(original, ZipArchiveMode.Update))
        {
            apk.CreateEntryFromFile(Path.Combine(root, "originaldex", "classes.dex"), "classes.dex");
            foreach (var name in new[] { "libunity.so", "libil2cpp.so" })
            {
                using var stream = apk.CreateEntry("lib/arm64-v8a/" + name, CompressionLevel.NoCompression).Open();
                stream.Write(elf);
            }
        }
        string sdk = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? throw new Exception("ANDROID_HOME is missing");
        string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME") ?? throw new Exception("JAVA_HOME is missing");
        var options = new PatchOptions(original, exported, payload, unity, Path.Combine(javaHome, "bin", "java.exe"),
            Path.Combine(root, "dexpatcher.jar"), Path.Combine(sdk, "build-tools", "35.0.0"),
            Path.Combine(root, "test.keystore"), "androiddebugkey", "android", null);
        await new ApkPatcher(new ToolRunner()).PatchAsync(options);
        using (var apk = ZipFile.OpenRead(exported))
        using (var patched = apk.GetEntry("classes.dex")!.Open())
        using (var buffer = new MemoryStream())
        {
            patched.CopyTo(buffer);
            if (buffer.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "originaldex", "classes.dex"))))
                throw new Exception("Real DexPatcher did not change classes.dex.");
            if (apk.GetEntry("assets/melonloader/etc/MelonLoader.dll") is null)
                throw new Exception("Missing payload in signed APK.");
        }
        Console.WriteLine("Real DexPatcher, zipalign, apksigner and signature/alignment verification passed on a synthetic APK.");
    }
}
