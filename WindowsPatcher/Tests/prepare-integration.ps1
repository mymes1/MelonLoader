$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP 'melon-patcher-integration'
New-Item -ItemType Directory -Force $root, "$root/original-src/sample", "$root/patch-src/sample", "$root/original-classes", "$root/patch-classes", "$root/originaldex", "$root/patchdex" | Out-Null
$sdk = $env:ANDROID_HOME
if (!$sdk) { throw 'ANDROID_HOME is required' }
$tools = Join-Path $sdk 'build-tools/35.0.0'
$android = Join-Path $sdk 'platforms/android-35/android.jar'
$annotation = Join-Path $PWD 'JavaBindings/deps/dexpatcher_annotations.jar'
@'
package sample;
public class Example { public void run() { } }
'@ | Set-Content -Encoding utf8NoBOM "$root/original-src/sample/Example.java"
@'
package sample;
import lanchon.dexpatcher.annotation.DexEdit;
import lanchon.dexpatcher.annotation.DexPrepend;
@DexEdit
public class Example { @DexPrepend public void run() { System.out.println("patched"); } }
'@ | Set-Content -Encoding utf8NoBOM "$root/patch-src/sample/Example.java"
@'
<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="test.melon.patcher">
  <uses-sdk android:minSdkVersion="23" android:targetSdkVersion="28"/>
  <application android:label="Fixture" android:hasCode="true"/>
</manifest>
'@ | Set-Content -Encoding utf8NoBOM "$root/AndroidManifest.xml"
& javac --release 8 -d "$root/original-classes" "$root/original-src/sample/Example.java"
if ($LASTEXITCODE -ne 0) { throw 'javac original failed' }
& javac --release 8 -cp $annotation -d "$root/patch-classes" "$root/patch-src/sample/Example.java"
if ($LASTEXITCODE -ne 0) { throw 'javac patch failed' }
& jar --create --file "$root/original.jar" -C "$root/original-classes" .
if ($LASTEXITCODE -ne 0) { throw 'jar original failed' }
& jar --create --file "$root/patch.jar" -C "$root/patch-classes" .
if ($LASTEXITCODE -ne 0) { throw 'jar patch failed' }
& "$tools/d8.bat" --min-api 23 --lib $android --output "$root/originaldex" "$root/original.jar"
if ($LASTEXITCODE -ne 0) { throw 'd8 original failed' }
& "$tools/d8.bat" --min-api 23 --lib $android --classpath $annotation --output "$root/patchdex" "$root/patch.jar"
if ($LASTEXITCODE -ne 0) { throw 'd8 patch failed' }
& "$tools/aapt2.exe" link -o "$root/base.apk" --manifest "$root/AndroidManifest.xml" -I $android
if ($LASTEXITCODE -ne 0) { throw 'aapt2 failed' }
& keytool -genkeypair -keystore "$root/test.keystore" -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 2 -dname 'CN=Test' -noprompt
if ($LASTEXITCODE -ne 0) { throw 'keytool failed' }
Invoke-WebRequest -Uri 'https://github.com/DexPatcher/dexpatcher-tool/releases/download/v1.7.0/dexpatcher-1.7.0.jar' -OutFile "$root/dexpatcher.jar"
Write-Host "Synthetic integration fixture: $root"
