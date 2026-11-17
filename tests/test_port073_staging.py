import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("port073_stage", ROOT / "AndroidPort073/stage.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PortStagingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.base = Path(self.tmp.name)
        self.managed = self.base / "managed"
        self.dotnet = self.base / "runtime"
        self.bootstrap = self.base / "bootstrap.so"
        self.bridge = self.base / "bridge.so"
        self.output = self.base / "output"
        for file in (self.managed / "net6/MelonLoader.dll",
                     self.managed / "net6/MelonLoader.NativeHost.dll",
                     self.managed / "net6/MelonLoader.runtimeconfig.json",
                     self.managed / "Dependencies/SupportModules/Il2Cpp.dll",
                     self.dotnet / "host/fxr/8.0.6/libhostfxr.so",
                     self.dotnet / "shared/Microsoft.NETCore.App/8.0.6/libcoreclr.so",
                     self.bootstrap, self.bridge):
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(b"fixture")

    def tearDown(self):
        self.tmp.cleanup()

    def stage(self):
        module.stage(self.managed, self.dotnet, self.bootstrap, self.bridge, self.output)

    def test_layout_and_no_overwrite(self):
        self.stage()
        self.assertTrue((self.output / "assets/MelonLoader/net6/MelonLoader.dll").is_file())
        self.assertTrue((self.output / "assets/dotnet/host/fxr/8.0.6/libhostfxr.so").is_file())
        self.assertTrue((self.output / "lib/arm64-v8a/libMelonLoader.Bootstrap.so").is_file())
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.stage()

    def test_missing_runtime_never_creates_output(self):
        (self.dotnet / "host/fxr/8.0.6/libhostfxr.so").unlink()
        with self.assertRaisesRegex(ValueError, "libhostfxr"):
            self.stage()
        self.assertFalse(self.output.exists())

    def test_symlink_in_payload_is_rejected(self):
        (self.managed / "net6/link").symlink_to(self.bridge)
        with self.assertRaisesRegex(ValueError, "Symlinks"):
            self.stage()
        self.assertFalse(self.output.exists())

    def test_output_cannot_be_inside_input(self):
        self.output = self.managed / "nested/output"
        with self.assertRaisesRegex(ValueError, "outside"):
            self.stage()
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
