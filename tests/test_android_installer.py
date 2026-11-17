"""Packaging contract tests with fake build outputs/tools; these do not test Android."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile

REPO = Path(__file__).resolve().parents[1]


class InstallerTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="melon test ")
        self.root = Path(self.tmp.name)
        self.installer = self.root / "AndroidInstaller"
        shutil.copytree(REPO / "AndroidInstaller", self.installer,
                        ignore=shutil.ignore_patterns(".env", "out", "dependencies"))
        self.tools = self.root / "tools"
        self.tools.mkdir()
        self.unity = self.root / "Unity"
        self.managed = self.unity / "Editor/Data/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Managed"
        self.managed.mkdir(parents=True)
        (self.managed / "UnityEngine.dll").write_bytes(b"unity managed")
        self.apk = self.root / "game with spaces.apk"
        with zipfile.ZipFile(self.apk, "w") as apk:
            apk.writestr("classes.dex", b"old dex")
            apk.writestr("META-INF/OLD.SF", b"signature")
            apk.writestr("lib/arm64-v8a/libunity.so", b"original unity")
            apk.writestr("lib/arm64-v8a/libil2cpp.so", b"il2cpp")
        self.write("mono-bcl/mscorlib.dll")
        self.write("mono-lib/libmono.so")
        self.write("bhaptics.so")
        self.write("keystore")
        self.write("JavaBindings/build/dex/classes.dex")
        self.write("Output/Debug/Android/MelonLoader/net4.7.2/MelonLoader.dll")
        self.write("Output/Debug/Android/MelonLoader/Dependencies/SupportModules/net4.7.2/Il2Cpp.dll")
        self.write("Output/Debug/AnyCPU/MelonLoader/Dependencies/Il2CppAssemblyGenerator/net4.7.2/Il2CppAssemblyGenerator.dll")
        for path in ("libBootstrap.so", "funchook/libfunchook.so", "capstone/libcapstone.so"):
            self.write("Bootstrap/.cmake/android/debug/" + path)
        for path in ("AssemblyUnhollower/AssemblyUnhollower.dll", "AssemblyUnhollower/Iced.dll",
                     "UnhollowerBaseLib/UnhollowerBaseLib.dll", "UnhollowerRuntimeLib/UnhollowerRuntimeLib.dll"):
            project, name = path.split("/")
            self.write(f"external/Il2CppAssemblyUnhollower/{project}/bin/Debug/net4.7.2/{name}")
        self.tool("dexpatcher", 'cp "$3/classes.dex" "$2/classes.dex"')
        self.tool("zipalign", 'cp "$4" "$5"')
        self.tool("apksigner", '''if [ "$1" = verify ]; then exit 0; fi
while [ "$1" != --out ]; do shift; done
cp "$3" "$2"''')
        values = {
            "ANDROID_SDK_BASE": self.tools, "TARGET_APK": self.apk,
            "UNITY_INSTALLATION_BASE": self.unity, "DEXPATCHER": self.tools / "dexpatcher",
            "MONO_BCL_DIR": self.root / "mono-bcl", "MONO_LIB_DIR": self.root / "mono-lib",
            "BHAPTICS_LIB": self.root / "bhaptics.so", "KEYSTORE": self.root / "keystore",
            "KEY_ALIAS": "test",
        }
        (self.installer / ".env").write_text("\n".join(f'{k}="{v}"' for k, v in values.items()))

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, path):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(b"test")

    def tool(self, name, script):
        path = self.tools / name
        path.write_text("#!/bin/sh\nset -eu\n" + script + "\n")
        path.chmod(0o755)

    def run_installer(self):
        return subprocess.run(["bash", str(self.installer / "install.sh")],
                              env={**os.environ, "KEYSTORE_PASSWORD": "test"},
                              text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

    def test_repackages_without_replacing_game_unity(self):
        result = self.run_installer()
        self.assertEqual(result.returncode, 0, result.stdout)
        with zipfile.ZipFile(self.installer / "out" / self.apk.name) as apk:
            self.assertEqual(apk.read("lib/arm64-v8a/libunity.so"), b"original unity")
            self.assertEqual(apk.read("classes.dex"), b"old dex")
            self.assertNotIn("META-INF/OLD.SF", apk.namelist())
            self.assertIn("lib/arm64-v8a/libcapstone.so", apk.namelist())
            self.assertIn("assets/melonloader/etc/managed/mscorlib.dll", apk.namelist())
            self.assertNotIn("assets/melonloader/etc/assembly_generation/managed/libcapstone.so", apk.namelist())

    def test_missing_dependencies_does_not_remove_existing_output(self):
        sentinel = self.installer / "out/previous.apk"
        sentinel.parent.mkdir()
        sentinel.write_bytes(b"previous")
        (self.root / "bhaptics.so").unlink()
        result = self.run_installer()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Missing required file", result.stdout)
        self.assertEqual(sentinel.read_bytes(), b"previous")


if __name__ == "__main__":
    unittest.main()
