#!/usr/bin/env bash
set -euo pipefail

BASE_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
PROJECT_DIR=$(cd -- "$BASE_DIR/.." && pwd)
# These paths refer to separately built Android dependencies, not desktop releases.
: "${MONO_BCL_DIR:?Set MONO_BCL_DIR to the Android Mono BCL directory}"
: "${MONO_LIB_DIR:?Set MONO_LIB_DIR to the arm64 Android Mono library directory}"
: "${BHAPTICS_LIB:?Set BHAPTICS_LIB to the arm64 bHaptics shared library}"

CORE="$PROJECT_DIR/Output/Debug/Android/MelonLoader/net4.7.2/MelonLoader.dll"
SUPPORT="$PROJECT_DIR/Output/Debug/Android/MelonLoader/Dependencies/SupportModules/net4.7.2"
GEN="$PROJECT_DIR/Output/Debug/AnyCPU/MelonLoader/Dependencies/Il2CppAssemblyGenerator/net4.7.2"
UNHOLLOWER="$PROJECT_DIR/external/Il2CppAssemblyUnhollower"
BOOTSTRAP="$PROJECT_DIR/Bootstrap/.cmake/android/debug"
DEX="$PROJECT_DIR/JavaBindings/build/dex"

shopt -s nullglob
bcl=("$MONO_BCL_DIR"/*.dll)
mono=("$MONO_LIB_DIR"/*.so)
support=("$SUPPORT"/*.dll)
generator=("$GEN"/*.dll)
dex=("$DEX"/*.dex)
for group in bcl mono support generator dex; do
  declare -n files="$group"
  ((${#files[@]})) || { echo "Missing $group files; check build outputs and .env" >&2; exit 1; }
done
extra=("$CORE" "$BHAPTICS_LIB" "$BOOTSTRAP/libBootstrap.so"
  "$BOOTSTRAP/funchook/libfunchook.so" "$BOOTSTRAP/capstone/libcapstone.so"
  "$UNHOLLOWER/AssemblyUnhollower/bin/Debug/net4.7.2/AssemblyUnhollower.dll"
  "$UNHOLLOWER/AssemblyUnhollower/bin/Debug/net4.7.2/Iced.dll"
  "$UNHOLLOWER/UnhollowerBaseLib/bin/Debug/net4.7.2/UnhollowerBaseLib.dll"
  "$UNHOLLOWER/UnhollowerRuntimeLib/bin/Debug/net4.7.2/UnhollowerRuntimeLib.dll")
for file in "${extra[@]}"; do
  [[ -f "$file" ]] || { echo "Missing required file: $file" >&2; exit 1; }
done

OUTPUT_BASE="$BASE_DIR/dependencies"
rm -rf -- "$OUTPUT_BASE"
mkdir -p "$OUTPUT_BASE"/{dex,mono/bcl,core,support_modules,assembly_generation,native}
cp -- "${dex[@]}" "$OUTPUT_BASE/dex/"
cp -- "${bcl[@]}" "$OUTPUT_BASE/mono/bcl/"
cp -- "$CORE" "$OUTPUT_BASE/core/"
cp -- "${support[@]}" "$OUTPUT_BASE/support_modules/"
cp -- "${generator[@]}" "${extra[@]:5}" "$OUTPUT_BASE/assembly_generation/"
cp -- "${extra[@]:1:4}" "${mono[@]}" "$OUTPUT_BASE/native/"
echo "Prepared legacy Android dependencies in $OUTPUT_BASE"
