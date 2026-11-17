#!/usr/bin/env python3
"""Stage prebuilt 0.7.3 components for the experimental Android entry point.

This does not patch an APK, build a runtime, or establish binary compatibility.
"""
import argparse
from pathlib import Path
import shutil
import tempfile


def require_file(path):
    if not path.is_file() or path.is_symlink():
        raise ValueError(f"Required regular file not found: {path}")


def copy_tree(source, destination):
    if not source.is_dir() or source.is_symlink():
        raise ValueError(f"Required directory not found: {source}")
    for path in source.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"Symlinks are not supported: {path}")
    shutil.copytree(source, destination)


def stage(managed, dotnet, bootstrap, bridge, dex, interop, output):
    managed, dotnet, bootstrap, bridge, dex, interop, output = map(
        lambda p: Path(p).absolute(), (managed, dotnet, bootstrap, bridge, dex, interop, output))
    for relative in ("net6/MelonLoader.dll", "net6/MelonLoader.NativeHost.dll",
                     "net6/MelonLoader.runtimeconfig.json", "Dependencies/SupportModules/Il2Cpp.dll",
                     "Dependencies/Il2CppAssemblyGenerator/Il2CppAssemblyGenerator.dll"):
        require_file(managed / relative)
    for path in (bootstrap, bridge, dex / "classes.dex", interop / "melon-interop-manifest.txt"):
        require_file(path)
    if not any((dotnet / "host/fxr").glob("*/libhostfxr.so")):
        raise ValueError("Android dotnet root must contain host/fxr/<version>/libhostfxr.so")
    if not any((dotnet / "shared/Microsoft.NETCore.App").glob("*/libcoreclr.so")):
        raise ValueError("Android dotnet root must contain shared/Microsoft.NETCore.App/<version>/libcoreclr.so")
    for source in (managed, dotnet, dex, interop):
        if output.parent.resolve().is_relative_to(source.resolve()):
            raise ValueError("Output must be outside the input trees")
    if output.exists() or output.is_symlink():
        raise ValueError(f"Output already exists (will not overwrite): {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".melonloader073-", dir=output.parent) as tmp:
        staged = Path(tmp) / "payload"
        copy_tree(managed, staged / "assets/MelonLoader")
        target_interop = staged / "assets/MelonLoader/Il2CppAssemblies"
        if target_interop.exists():
            raise ValueError("Managed input must not contain Il2CppAssemblies")
        copy_tree(interop, target_interop)
        copy_tree(dotnet, staged / "assets/dotnet")
        copy_tree(dex, staged / "dex")
        native = staged / "lib/arm64-v8a"
        native.mkdir(parents=True)
        shutil.copyfile(bootstrap, native / "libMelonLoader.Bootstrap.so")
        shutil.copyfile(bridge, native / "libmelonloader073_bridge.so")
        staged.rename(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("managed", "dotnet", "bootstrap", "bridge", "dex", "interop", "output"):
        parser.add_argument("--" + option, required=True, type=Path)
    args = parser.parse_args()
    try:
        stage(args.managed, args.dotnet, args.bootstrap, args.bridge, args.dex, args.interop, args.output)
    except (OSError, ValueError) as error:
        parser.exit(1, f"error: {error}\n")


if __name__ == "__main__":
    main()
