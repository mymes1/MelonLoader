#!/usr/bin/env bash
set -euo pipefail

BASE_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
[[ -f "$BASE_DIR/.env" ]] || { echo "Copy AndroidInstaller/.env.example to .env and configure it first" >&2; exit 1; }
# shellcheck source=/dev/null
source "$BASE_DIR/.env"
: "${ANDROID_SDK_BASE:?Set ANDROID_SDK_BASE to an Android SDK build-tools directory}"
: "${TARGET_APK:?Set TARGET_APK to the original APK}"
: "${UNITY_INSTALLATION_BASE:?Set UNITY_INSTALLATION_BASE to the matching Unity editor}"
: "${DEXPATCHER:?Set DEXPATCHER to the dexpatcher executable}"
: "${KEYSTORE:?Set KEYSTORE to your signing keystore}"
: "${KEY_ALIAS:?Set KEY_ALIAS to your signing key alias}"
: "${KEYSTORE_PASSWORD:?Export KEYSTORE_PASSWORD before running the installer}"
export KEYSTORE_PASSWORD
if [[ -n ${KEY_PASSWORD:-} ]]; then export KEY_PASSWORD; fi

[[ -x "$DEXPATCHER" && -f "$KEYSTORE" && -x "$ANDROID_SDK_BASE/zipalign" && -x "$ANDROID_SDK_BASE/apksigner" ]] || {
  echo "Missing dexpatcher, keystore, zipalign, or apksigner; check .env" >&2; exit 1;
}
TARGET_APK=$(realpath -e -- "$TARGET_APK")
KEYSTORE=$(realpath -e -- "$KEYSTORE")
OUTPUT_DIR="$BASE_DIR/out"
for input in "$TARGET_APK" "$KEYSTORE"; do
  case "$input" in "$OUTPUT_DIR"/*) echo "Input $input must not be inside $OUTPUT_DIR" >&2; exit 1;; esac
done
UNITY_MANAGED="$UNITY_INSTALLATION_BASE/Editor/Data/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Managed"
shopt -s nullglob
unity_dlls=("$UNITY_MANAGED"/*.dll)
((${#unity_dlls[@]})) || { echo "No Unity managed DLLs found in $UNITY_MANAGED" >&2; exit 1; }

unzip -tq "$TARGET_APK" > /dev/null
mapfile -t entries < <(unzip -Z1 "$TARGET_APK")
dex_entries=()
has_il2cpp=0
has_unity=0
for entry in "${entries[@]}"; do
  [[ $entry =~ ^classes([0-9]+)?\.dex$ ]] && dex_entries+=("$entry")
  [[ $entry == lib/arm64-v8a/libil2cpp.so ]] && has_il2cpp=1
  [[ $entry == lib/arm64-v8a/libunity.so ]] && has_unity=1
done
((${#dex_entries[@]} && has_il2cpp && has_unity)) || {
  echo "Expected a single Unity IL2CPP APK with root dex files and arm64-v8a libil2cpp.so and libunity.so" >&2; exit 1;
}
export MONO_BCL_DIR MONO_LIB_DIR BHAPTICS_LIB
bash "$BASE_DIR/prepare-dependencies.sh"
DEPENDENCIES_DIR="$BASE_DIR/dependencies"
rm -rf -- "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"/{source,patched,work}
MODIFIED_APK="$OUTPUT_DIR/$(basename -- "$TARGET_APK")"
cp -- "$TARGET_APK" "$OUTPUT_DIR/work/unsigned.apk"
unzip -jo "$TARGET_APK" "${dex_entries[@]}" -d "$OUTPUT_DIR/source"
"$DEXPATCHER" --output "$OUTPUT_DIR/patched" "$OUTPUT_DIR/source" "$DEPENDENCIES_DIR/dex"
[[ -f "$OUTPUT_DIR/patched/classes.dex" ]] || { echo "DexPatcher did not produce classes.dex" >&2; exit 1; }

PATCHED="$OUTPUT_DIR/patched"
mkdir -p "$PATCHED/assets/melonloader/etc/managed" "$PATCHED/assets/melonloader/etc/support" \
  "$PATCHED/assets/melonloader/etc/assembly_generation/managed" \
  "$PATCHED/assets/melonloader/etc/assembly_generation/unity" "$PATCHED/lib/arm64-v8a"
cp -- "$DEPENDENCIES_DIR"/mono/bcl/*.dll "$PATCHED/assets/melonloader/etc/managed/"
cp -- "$DEPENDENCIES_DIR"/core/* "$PATCHED/assets/melonloader/etc/"
cp -- "$DEPENDENCIES_DIR"/support_modules/* "$PATCHED/assets/melonloader/etc/support/"
cp -- "$DEPENDENCIES_DIR"/assembly_generation/* "$PATCHED/assets/melonloader/etc/assembly_generation/managed/"
cp -- "${unity_dlls[@]}" "$PATCHED/assets/melonloader/etc/assembly_generation/unity/"
cp -- "$DEPENDENCIES_DIR"/native/*.so "$PATCHED/lib/arm64-v8a/"

# Preserve the game's own libunity.so. Remove old dex and v1 signatures before repacking.
zip -d "$OUTPUT_DIR/work/unsigned.apk" "${dex_entries[@]}" 'META-INF/*'
# Store new native libraries uncompressed; zipalign must run before apksigner.
(cd "$PATCHED" && zip -0 -r "$OUTPUT_DIR/work/unsigned.apk" .)
"$ANDROID_SDK_BASE/zipalign" -p -f 4 "$OUTPUT_DIR/work/unsigned.apk" "$OUTPUT_DIR/work/aligned.apk"
sign_args=(sign --ks "$KEYSTORE" --ks-key-alias "$KEY_ALIAS" --ks-pass env:KEYSTORE_PASSWORD)
if [[ -n ${KEY_PASSWORD:-} ]]; then sign_args+=(--key-pass env:KEY_PASSWORD); fi
"$ANDROID_SDK_BASE/apksigner" "${sign_args[@]}" --out "$OUTPUT_DIR/work/signed.apk" "$OUTPUT_DIR/work/aligned.apk"
"$ANDROID_SDK_BASE/apksigner" verify --verbose "$OUTPUT_DIR/work/signed.apk"
mv -- "$OUTPUT_DIR/work/signed.apk" "$MODIFIED_APK"
echo "Signed APK: $MODIFIED_APK"
