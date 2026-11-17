using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MelonPatcher.Core;

/// <summary>Packages the legacy 0.4.1 Android payload. This does not support the unfinished 0.7.3 port.</summary>
public sealed partial class ApkPatcher(IToolRunner runner)
{
    private const string StorePasswordVariable = "MELON_PATCHER_STORE_PASSWORD";
    private const string KeyPasswordVariable = "MELON_PATCHER_KEY_PASSWORD";
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    [GeneratedRegex(@"^classes(?:[2-9]|[1-9][0-9]+)?\.dex$", RegexOptions.CultureInvariant)]
    private static partial Regex DexName();

    public async Task PatchAsync(PatchOptions options, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Validate(options);
        string workspace = Path.Combine(Path.GetTempPath(), "MelonPatcher", Guid.NewGuid().ToString("N"));
        string candidate = Path.Combine(Path.GetDirectoryName(plan.Output)!, $".melonpatch-{Guid.NewGuid():N}.apk");
        Directory.CreateDirectory(workspace);
        try
        {
            var sourceDir = Path.Combine(workspace, "original-dex");
            var patchedDir = Path.Combine(workspace, "patched-dex");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(patchedDir);
            progress?.Report("Reading and validating APK...");
            using (var apk = ZipFile.OpenRead(plan.Input))
            {
                ValidateApk(apk, plan.PayloadFiles);
                foreach (var entry in apk.Entries.Where(e => DexName().IsMatch(e.FullName)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.Length > 512L * 1024 * 1024) throw new InvalidDataException("Dex file exceeds 512 MB.");
                    entry.ExtractToFile(Path.Combine(sourceDir, entry.FullName));
                }
            }

            progress?.Report("Patching dex with DexPatcher...");
            await runner.RunAsync(plan.Java, ["-jar", plan.DexPatcher, "--multi-dex", "--output", patchedDir, sourceDir, plan.DexDirectory],
                null, cancellationToken);
            var patchedDex = Directory.GetFiles(patchedDir, "*.dex", SearchOption.TopDirectoryOnly)
                .Where(p => DexName().IsMatch(Path.GetFileName(p))).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (!patchedDex.Any(p => Path.GetFileName(p) == "classes.dex") || patchedDex.Any(p => new FileInfo(p).Length == 0))
                throw new InvalidDataException("DexPatcher did not produce a non-empty classes.dex.");

            string unsigned = Path.Combine(workspace, "unsigned.apk");
            string aligned = Path.Combine(workspace, "aligned.apk");
            string signed = Path.Combine(workspace, "signed.apk");
            progress?.Report("Repacking APK (the game's libunity.so is preserved)...");
            Repack(plan, unsigned, patchedDex, cancellationToken);

            progress?.Report("Aligning native libraries for 16 KB and 4 KB page sizes...");
            await runner.RunAsync(plan.Zipalign, ["-f", "-P", "16", "4", unsigned, aligned], null, cancellationToken);
            var secrets = new Dictionary<string, string> { [StorePasswordVariable] = options.KeystorePassword };
            var signArgs = new List<string> { "-jar", plan.ApkSigner, "sign", "--ks", plan.Keystore,
                "--ks-key-alias", options.KeyAlias, "--ks-pass", $"env:{StorePasswordVariable}" };
            if (!string.IsNullOrEmpty(options.KeyPassword))
            {
                secrets.Add(KeyPasswordVariable, options.KeyPassword);
                signArgs.AddRange(["--key-pass", $"env:{KeyPasswordVariable}"]);
            }
            signArgs.AddRange(["--out", signed, aligned]);
            progress?.Report("Signing APK...");
            await runner.RunAsync(plan.Java, signArgs, secrets, cancellationToken);
            progress?.Report("Verifying signature and alignment...");
            await runner.RunAsync(plan.Java, ["-jar", plan.ApkSigner, "verify", "--verbose", signed], null, cancellationToken);
            await runner.RunAsync(plan.Zipalign, ["-c", "-P", "16", "4", signed], null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Copy to the output volume first, then rename without replacing existing files.
            File.Copy(signed, candidate, overwrite: false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(candidate, plan.Output, overwrite: false);
            progress?.Report($"Exported: {plan.Output}");
        }
        finally
        {
            try { if (File.Exists(candidate)) File.Delete(candidate); } catch (IOException) { }
            try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); } catch (IOException) { }
        }
    }

    private sealed record Plan(string Input, string Output, string Java, string DexPatcher,
        string DexDirectory, string Zipalign, string ApkSigner, string Keystore,
        IReadOnlyDictionary<string, string> PayloadFiles);

    private static Plan Validate(PatchOptions o)
    {
        string FileRequired(string path, string description)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"{description} not found.", path);
            return Path.GetFullPath(path);
        }
        string input = FileRequired(o.InputApk, "Input APK");
        if (string.IsNullOrWhiteSpace(o.OutputApk)) throw new ArgumentException("Choose an output APK.");
        string output = Path.GetFullPath(o.OutputApk);
        if (!output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must end in .apk.");
        if (PathComparer.Equals(input, output) || File.Exists(output))
            throw new IOException("Output must be a new file, different from the input APK.");
        if (!Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException("Output directory does not exist.");
        string java = FileRequired(o.JavaExecutable, "Java executable");
        string patcher = FileRequired(o.DexPatcherJar, "DexPatcher JAR");
        string zipalign = FileRequired(Path.Combine(o.BuildToolsDirectory, "zipalign.exe"), "Android zipalign.exe");
        string signer = FileRequired(Path.Combine(o.BuildToolsDirectory, "lib", "apksigner.jar"), "Android apksigner.jar");
        string keystore = FileRequired(o.Keystore, "Keystore");
        if (string.IsNullOrWhiteSpace(o.KeyAlias) || string.IsNullOrEmpty(o.KeystorePassword))
            throw new ArgumentException("A key alias and keystore password are required.");
        if (!Directory.Exists(o.PayloadDirectory)) throw new DirectoryNotFoundException("Legacy payload directory not found.");
        if (!Directory.Exists(o.UnityManagedDirectory)) throw new DirectoryNotFoundException("Unity managed DLL directory not found.");
        string payload = Path.GetFullPath(o.PayloadDirectory);
        string dex = Path.Combine(payload, "dex");
        if (!Directory.Exists(dex) || !Directory.GetFiles(dex, "*.dex").Any(p => DexName().IsMatch(Path.GetFileName(p))))
            throw new FileNotFoundException("Legacy payload dex files are missing.");

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void AddFiles(string source, string pattern, string destination, params string[] required)
        {
            string dir = Path.Combine(payload, source);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"Missing payload directory: {source}");
            foreach (string filename in required) FileRequired(Path.Combine(dir, filename), $"Payload {source}/{filename}");
            var paths = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
            if (paths.Length == 0) throw new FileNotFoundException($"No {pattern} files in payload {source}.");
            foreach (var path in paths)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Payload symlinks are not supported: {path}");
                string name = Path.GetFileName(path);
                if (name.Equals("libunity.so", StringComparison.OrdinalIgnoreCase) || name.Equals("libil2cpp.so", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The payload must not replace the game's {name}.");
                files.Add(destination + name, path);
            }
        }
        string coreFile = FileRequired(Path.Combine(payload, "core", "MelonLoader.dll"), "Legacy MelonLoader.dll");
        try
        {
            var identity = AssemblyName.GetAssemblyName(coreFile);
            if (identity.Name != "MelonLoader" || identity.Version != new Version(0, 4, 1, 0))
                throw new InvalidDataException("This patcher requires the legacy MelonLoader 0.4.1 payload, not a desktop or 0.7.3 payload.");
        }
        catch (BadImageFormatException ex) { throw new InvalidDataException("Payload MelonLoader.dll is not a managed assembly.", ex); }
        AddFiles("core", "*.dll", "assets/melonloader/etc/", "MelonLoader.dll");
        AddFiles(Path.Combine("mono", "bcl"), "*.dll", "assets/melonloader/etc/managed/", "mscorlib.dll");
        AddFiles("support_modules", "*.dll", "assets/melonloader/etc/support/", "Il2Cpp.dll");
        AddFiles("assembly_generation", "*.dll", "assets/melonloader/etc/assembly_generation/managed/",
            "Il2CppAssemblyGenerator.dll", "AssemblyUnhollower.dll", "UnhollowerBaseLib.dll", "UnhollowerRuntimeLib.dll", "Iced.dll");
        AddFiles("native", "*.so", "lib/arm64-v8a/", "libBootstrap.so", "libfunchook.so", "libcapstone.so", "libbhaptics-native-lib.so");
        if (!Directory.GetFiles(Path.Combine(payload, "native"), "libmono*.so").Any())
            throw new FileNotFoundException("Payload must include arm64 Android Mono native libraries.");
        Span<byte> header = stackalloc byte[20];
        foreach (string so in files.Where(pair => pair.Key.EndsWith(".so", StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value))
        {
            using var stream = File.OpenRead(so);
            if (stream.Length < header.Length)
                throw new InvalidDataException($"Native library is not a little-endian arm64 ELF: {Path.GetFileName(so)}");
            stream.ReadExactly(header);
            if (!header[..4].SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' })
                || header[4] != 2 || header[5] != 1 || header[18] != 0xb7 || header[19] != 0)
                throw new InvalidDataException($"Native library is not a little-endian arm64 ELF: {Path.GetFileName(so)}");
        }
        var unityFiles = Directory.GetFiles(o.UnityManagedDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        if (unityFiles.Length == 0) throw new FileNotFoundException("No Unity managed DLLs found.");
        foreach (var path in unityFiles)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unity DLL symlinks are not supported.");
            files.Add("assets/melonloader/etc/assembly_generation/unity/" + Path.GetFileName(path), path);
        }
        return new Plan(input, output, java, patcher, dex, zipalign, signer, keystore, files);
    }

    private static void ValidateApk(ZipArchive apk, IReadOnlyDictionary<string, string> payload)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in apk.Entries)
        {
            string name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException($"Unsafe ZIP entry: {name}");
            if (!names.Add(name)) throw new InvalidDataException($"Duplicate ZIP entry: {name}");
            if (payload.ContainsKey(name) || name.StartsWith("assets/melonloader/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"APK already contains a loader or a conflicting payload entry: {name}");
            if (name.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("lib/arm64-v8a/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only standalone arm64-v8a APKs are supported; other ABIs were found.");
        }
        foreach (var required in new[] { "AndroidManifest.xml", "classes.dex", "lib/arm64-v8a/libunity.so", "lib/arm64-v8a/libil2cpp.so" })
            if (!names.Contains(required)) throw new InvalidDataException($"Not a supported standalone Unity IL2CPP arm64 APK: {required} missing.");
    }

    private static bool IsSignature(string name)
    {
        if (!name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) return false;
        string sub = name[9..];
        if (sub.Contains('/')) return false;
        return sub.Equals("MANIFEST.MF", StringComparison.OrdinalIgnoreCase)
            || sub.StartsWith("SIG-", StringComparison.OrdinalIgnoreCase)
            || new[] { ".SF", ".RSA", ".DSA", ".EC" }.Any(ext => sub.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static void Repack(Plan plan, string output, string[] dex, CancellationToken ct)
    {
        using var source = ZipFile.OpenRead(plan.Input);
        using var target = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var entry in source.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (DexName().IsMatch(entry.FullName) || IsSignature(entry.FullName)) continue;
            // Android requires resources.arsc to be stored on modern devices.
            // Preserve other stored entries (notably Unity streaming assets), and
            // always store native libraries so zipalign can page-align them.
            bool store = entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.Equals("resources.arsc", StringComparison.OrdinalIgnoreCase)
                || entry.CompressedLength == entry.Length;
            var dest = target.CreateEntry(entry.FullName, store ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            dest.LastWriteTime = entry.LastWriteTime;
            using var input = entry.Open();
            using var outputStream = dest.Open();
            input.CopyTo(outputStream);
        }
        foreach (var path in dex)
        {
            ct.ThrowIfCancellationRequested();
            target.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
        }
        foreach (var (name, path) in plan.PayloadFiles)
        {
            ct.ThrowIfCancellationRequested();
            target.CreateEntryFromFile(path, name, name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
        }
    }
}
