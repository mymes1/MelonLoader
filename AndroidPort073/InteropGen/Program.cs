using System.Diagnostics;
using System.Security.Cryptography;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Serialized;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;

// Offline generation: Cpp2IL runs on the desktop, never on Android. This tool
// makes no compatibility guarantee for the still-experimental 0.7.3 runtime.
try
{
    if (args.Length != 12 || args.Where((_, index) => index % 2 == 0).Distinct().Count() != 6)
        throw new ArgumentException("Usage: InteropGen --binary <libil2cpp.so> --metadata <global-metadata.dat> --unity-version <version> --unity-libs <directory> --cpp2il <executable> --output <new-directory>");
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
    string Get(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("Missing " + key);
    string binary = Path.GetFullPath(Get("--binary"));
    string metadata = Path.GetFullPath(Get("--metadata"));
    string unityVersion = Get("--unity-version");
    string unityLibs = Path.GetFullPath(Get("--unity-libs"));
    string cpp2il = Path.GetFullPath(Get("--cpp2il"));
    string output = Path.GetFullPath(Get("--output"));
    foreach (var file in new[] { binary, metadata, cpp2il })
        if (!File.Exists(file)) throw new FileNotFoundException("Required file missing", file);
    if (!Directory.Exists(unityLibs) || !Directory.EnumerateFiles(unityLibs, "*.dll").Any())
        throw new DirectoryNotFoundException("Unity managed libraries missing: " + unityLibs);
    if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists: " + output);
    string parent = Path.GetDirectoryName(output)!;
    Directory.CreateDirectory(parent);
    string work = Path.Combine(parent, ".melon-interop-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    try
    {
        string dumped = Path.Combine(work, "dumped"), generated = Path.Combine(work, "generated");
        var process = new ProcessStartInfo(cpp2il) { UseShellExecute = false, WorkingDirectory = work };
        foreach (string arg in new[] { "--force-binary-path", binary, "--force-metadata-path", metadata,
                     "--force-unity-version", unityVersion, "--output-as", "dummydll", "--output-to", dumped,
                     "--use-processor", "attributeanalyzer,attributeinjector" })
            process.ArgumentList.Add(arg);
        process.Environment["NO_COLOR"] = "1";
        using (var running = Process.Start(process) ?? throw new InvalidOperationException("Cannot start Cpp2IL"))
        {
            running.WaitForExit();
            if (running.ExitCode != 0) throw new InvalidOperationException("Cpp2IL failed: exit " + running.ExitCode);
        }
        var dlls = Directory.GetFiles(dumped, "*.dll", SearchOption.TopDirectoryOnly);
        if (dlls.Length == 0) throw new InvalidDataException("Cpp2IL produced no dummy DLLs");
        var resolver = new Resolver();
        var source = dlls.Select(f => ModuleDefinition.FromFile(f, new ModuleReaderParameters { ModuleResolver = resolver }))
            .Select(m => { resolver.Add(m); return m.Assembly!; }).ToList();
        var generatorOptions = new GeneratorOptions
        {
            GameAssemblyPath = binary,
            Source = source,
            OutputDir = generated,
            UnityBaseLibsDir = unityLibs,
            Parallel = true,
            Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptOut,
        };
        Il2CppInteropGenerator.Create(generatorOptions).AddInteropAssemblyGenerator().Run();
        var generatedDlls = Directory.GetFiles(generated, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        if (generatedDlls.Length == 0) throw new InvalidDataException("Il2CppInterop produced no proxy DLLs");
        static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var lines = new List<string> { "melonloader-073-interop-v1", "game " + Hash(binary), "metadata " + Hash(metadata) };
        foreach (string dll in generatedDlls)
        {
            string name = Path.GetFileName(dll);
            if (name.Contains('\n') || name.Contains('\r') || name.Contains('/') || name.Contains('\\'))
                throw new InvalidDataException("Invalid DLL filename");
            lines.Add("dll " + Hash(dll) + " " + name);
        }
        File.WriteAllLines(Path.Combine(generated, "melon-interop-manifest.txt"), lines);
        Directory.Move(generated, output);
        Console.WriteLine($"Generated {generatedDlls.Length} interop DLLs in {output}.");
    }
    finally { Directory.Delete(work, recursive: true); }
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    return 1;
}

internal sealed class Resolver : INetModuleResolver
{
    private readonly Dictionary<string, ModuleDefinition> modules = new();
    internal void Add(ModuleDefinition module) => modules[module.Name!] = module;
    public ModuleDefinition Resolve(string name) => modules.GetValueOrDefault(name)!;
    public void Dispose() => modules.Clear();
}
