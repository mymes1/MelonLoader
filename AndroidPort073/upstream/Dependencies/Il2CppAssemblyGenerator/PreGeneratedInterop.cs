using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MelonLoader.Il2CppAssemblyGenerator
{
    // Integrity and game identity check, not a signature or trust verification.
    internal static class PreGeneratedInterop
    {
        internal static bool Validate(string game, string metadata, string directory, out string error)
        {
            try
            {
                string[] lines = File.ReadAllLines(Path.Combine(directory, "melon-interop-manifest.txt"));
                if (lines.Length < 4 || lines[0] != "melonloader-073-interop-v1")
                    throw new InvalidDataException("Invalid interop manifest");
                VerifyHash(lines[1], "game ", game);
                VerifyHash(lines[2], "metadata ", metadata);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 3; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith("dll ", StringComparison.Ordinal) || line.Length < 70 || line[68] != ' ')
                        throw new InvalidDataException("Invalid interop DLL record");
                    string name = line.Substring(69);
                    if (name.Length == 0 || name != Path.GetFileName(name) || name.Contains('\\') ||
                        !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !names.Add(name))
                        throw new InvalidDataException("Invalid or repeated interop DLL name");
                    VerifyHash(line.Substring(0, 68), "dll ", Path.Combine(directory, name));
                }
                if (names.Count == 0 || Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        .Any(p => !names.Contains(Path.GetFileName(p))))
                    throw new InvalidDataException("Unexpected or missing interop DLLs");
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void VerifyHash(string record, string prefix, string file)
        {
            if (!record.StartsWith(prefix, StringComparison.Ordinal) || record.Length != prefix.Length + 64)
                throw new InvalidDataException("Invalid hash record");
            using var input = File.OpenRead(file);
            using var sha256 = SHA256.Create();
            string actual = Convert.ToHexString(sha256.ComputeHash(input));
            if (!string.Equals(record.Substring(prefix.Length), actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Hash mismatch: " + Path.GetFileName(file));
        }
    }
}
