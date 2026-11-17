using MelonLoader.Il2CppAssemblyGenerator;

if (args.Length != 3) throw new ArgumentException("Expected game binary, metadata and generated interop paths");
string game = args[0], metadata = args[1], dir = args[2];
void Expect(bool expected, string test)
{
    bool actual = PreGeneratedInterop.Validate(game, metadata, dir, out var error);
    if (actual != expected) throw new Exception($"{test}: got {actual}, error: {error}");
}
Expect(true, "generated interop");
string extra = Path.Combine(dir, "unexpected-test.dll");
File.WriteAllText(extra, "unexpected");
Expect(false, "untracked DLL");
File.Delete(extra);
string proxy = Directory.GetFiles(dir, "*.dll")[0];
using (var stream = new FileStream(proxy, FileMode.Append)) stream.WriteByte(0);
Expect(false, "modified DLL");
Console.WriteLine("Interop validator accepted matching outputs and rejected untracked/modified DLLs.");
