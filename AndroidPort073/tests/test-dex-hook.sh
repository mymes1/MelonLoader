#!/usr/bin/env bash
# Proves the patch is applied to a synthetic UnityPlayerActivity, not that a
# real Unity player or the unfinished 0.7.3 runtime can initialize.
set -euo pipefail
log="${RUNNER_TEMP:?}/melon073-hook-test.log"
# Surface diagnostics as annotations even when the Actions log download is unavailable.
trap 'status=$?; if [[ $status -ne 0 ]]; then tail -30 "$log" | while IFS= read -r line; do echo "::error::${line//%/%25}"; done; fi' EXIT
exec > >(tee "$log") 2>&1
root="${RUNNER_TEMP:?}/melon073-hook-test"
mkdir -p "$root/classes" "$root/input" "$root/output"
android="$ANDROID_HOME/platforms/android-35/android.jar"
build="$ANDROID_HOME/build-tools/35.0.0"
javac --release 8 -cp "$android" -d "$root/classes" \
  AndroidPort073/tests/fixtures/com/unity3d/player/UnityPlayerActivity.java
jar --create --file "$root/original.jar" -C "$root/classes" .
"$build/d8" --min-api 23 --lib "$android" --output "$root/input" "$root/original.jar"
curl --fail --location --silent --show-error \
  https://github.com/DexPatcher/dexpatcher-tool/releases/download/v1.7.0/dexpatcher-1.7.0.jar \
  -o "$root/dexpatcher.jar"
java -jar "$root/dexpatcher.jar" --multi-dex --output "$root/output" \
  "$root/input" "${RUNNER_TEMP}/melon073-components/dex"
"$build/dexdump" -d "$root/output/classes.dex" > "$root/disassembly.txt"
# Limit the search to Activity.onCreate rather than merely finding an unrelated
# reference to Loader073 elsewhere in the dex.
python3 - "$root/disassembly.txt" <<'PY'
from pathlib import Path
import re
import sys
text = Path(sys.argv[1]).read_text()
activity = text.split("Class descriptor  : 'Lcom/unity3d/player/UnityPlayerActivity;'", 1)[1]
activity = activity.split("Class #", 1)[0]
method = re.search(r"name\s+: 'onCreate'.*?(?=\n\s*#\d+\s+:|\Z)", activity, re.S)
assert method, 'patched onCreate not found'
assert 'Lcom/melonloader/port073/Loader073;.initialize' in method.group(), 'onCreate did not invoke Loader073.initialize'
assert 'Landroid/app/Activity;.onCreate' in method.group(), 'original activity body was lost'
PY
echo 'Synthetic UnityPlayerActivity contains the 0.7.3 lifecycle hook and original body.'
