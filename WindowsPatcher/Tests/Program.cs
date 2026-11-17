using MelonPatcher.Core;
using System.IO.Compression;

if (args.Length == 1)
{
    await RealIntegration.RunAsync(args[0]);
    return;
}

string root = Path.Combine(Path.GetTempPath(), "melonpatcher-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string Write(string name, byte[]? data = null)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data ?? [1, 2, 3]);
        return path;
    }
    byte[] elf = new byte[20];
    elf[0] = 0x7f; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
    elf[4] = 2; elf[5] = 1; elf[18] = 0xb7;
    Write("payload/mono/bcl/mscorlib.dll");
    Write("payload/support_modules/Il2Cpp.dll");
    Write("payload/dex/classes.dex");
    foreach (string name in new[] { "Il2CppAssemblyGenerator.dll", "AssemblyUnhollower.dll", "UnhollowerBaseLib.dll", "UnhollowerRuntimeLib.dll", "Iced.dll" })
        Write("payload/assembly_generation/" + name);
    foreach (string name in new[] { "libBootstrap.so", "libfunchook.so", "libcapstone.so", "libbhaptics-native-lib.so", "libmono.so" })
        Write("payload/native/" + name, elf);
    Directory.CreateDirectory(Path.Combine(root, "payload/core"));
    File.Copy(typeof(DummyLegacy.Marker).Assembly.Location, Path.Combine(root, "payload/core/MelonLoader.dll"));
    Write("unity/UnityEngine.dll");
    string java = Write("tools/java.exe"), dexPatcher = Write("tools/dexpatcher.jar"), keystore = Write("tools/key.keystore");
    Write("buildtools/zipalign.exe"); Write("buildtools/lib/apksigner.jar");
    string apkPath = Path.Combine(root, "input.apk"), outputPath = Path.Combine(root, "output.apk");
    using (var apk = ZipFile.Open(apkPath, ZipArchiveMode.Create))
    {
        void Add(string name, string text)
        {
            using var stream = new StreamWriter(apk.CreateEntry(name).Open());
            stream.Write(text);
        }
        Add("AndroidManifest.xml", "manifest");
        Add("classes.dex", "original");
        Add("classes2.dex", "original2");
        Add("lib/arm64-v8a/libunity.so", "original-unity");
        Add("lib/arm64-v8a/libil2cpp.so", "original-il2cpp");
        Add("META-INF/OLD.SF", "old-signature");
        Add("META-INF/services/test", "keep-metadata");
    }
    var options = new PatchOptions(apkPath, outputPath, Path.Combine(root, "payload"), Path.Combine(root, "unity"),
        java, dexPatcher, Path.Combine(root, "buildtools"), keystore, "alias", "secret", null);
    var runner = new FakeRunner();
    await new ApkPatcher(runner).PatchAsync(options);
    Check(runner.Steps.SequenceEqual(new[] { "dex", "align", "sign", "verify", "check-align" }), "wrong step order");
    Check(!runner.Arguments.Any(a => a.Contains("secret", StringComparison.Ordinal)), "password leaked into command arguments");
    using (var apk = ZipFile.OpenRead(outputPath))
    {
        string Read(string name) { using var reader = new StreamReader(apk.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        Check(Read("classes.dex") == "patched", "dex not patched");
        Check(apk.GetEntry("classes2.dex") == null, "old dex still present");
        Check(Read("lib/arm64-v8a/libunity.so") == "original-unity", "libunity was replaced");
        Check(apk.GetEntry("META-INF/OLD.SF") == null, "old signature retained");
        Check(Read("META-INF/services/test") == "keep-metadata", "non-signature metadata removed");
        Check(apk.GetEntry("assets/melonloader/etc/managed/mscorlib.dll") != null, "payload missing");
    }
    await ExpectFailure(() => new ApkPatcher(new FakeRunner()).PatchAsync(options), "existing output must not be overwritten");
    var sameInput = options with { OutputApk = apkPath };
    await ExpectFailure(() => new ApkPatcher(new FakeRunner()).PatchAsync(sameInput), "input must never be overwritten");
    string failOutput = Path.Combine(root, "failure.apk");
    await ExpectFailure(() => new ApkPatcher(new FakeRunner("verify")).PatchAsync(options with { OutputApk = failOutput }), "verification failure should fail");
    Check(!File.Exists(failOutput), "output exported despite failed verification");
    Write("payload/native/libBootstrap.so", [0, 1, 2]);
    await ExpectFailure(() => new ApkPatcher(new FakeRunner()).PatchAsync(options with { OutputApk = failOutput }), "bad native architecture should fail");
    Check(!File.Exists(failOutput), "output exported despite invalid payload");
    Console.WriteLine("Windows patcher packaging contract tests passed (external tools mocked).");
}
finally { Directory.Delete(root, true); }

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task ExpectFailure(Func<Task> action, string message)
{
    try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException) { return; }
    throw new Exception(message);
}

sealed class FakeRunner(string? failStep = null) : IToolRunner
{
    public List<string> Steps { get; } = [];
    public List<string> Arguments { get; } = [];
    public Task RunAsync(string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        Arguments.AddRange(args);
        string step;
        if (args.Contains("--output"))
        {
            step = "dex";
            if (!args.Contains("--multi-dex")) throw new Exception("multidex flag missing");
            File.WriteAllText(Path.Combine(args[args.IndexOf("--output") + 1], "classes.dex"), "patched");
        }
        else if (args.Contains("sign"))
        {
            step = "sign";
            if (environment is null || environment["MELON_PATCHER_STORE_PASSWORD"] != "secret") throw new Exception("missing password");
            File.Copy(args[^1], args[args.IndexOf("--out") + 1]);
        }
        else if (args.Contains("verify")) step = "verify";
        else if (args.Contains("-c")) step = "check-align";
        else
        {
            step = "align";
            File.Copy(args[^2], args[^1]);
        }
        Steps.Add(step);
        if (step == failStep) throw new InvalidOperationException("test tool failure");
        return Task.CompletedTask;
    }
}

static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++) if (EqualityComparer<T>.Default.Equals(list[i], item)) return i;
        return -1;
    }
}
