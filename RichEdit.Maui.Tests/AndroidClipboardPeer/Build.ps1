param(
    [string]$AndroidSdk = 'C:/Program Files (x86)/Android/android-sdk',
    [string]$OutputDirectory = '.codex/android-clipboard-peer'
)
$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutputDirectory)
$buildTools = Join-Path $AndroidSdk 'build-tools/36.1.0'
$platform = Join-Path $AndroidSdk 'platforms/android-36/android.jar'
New-Item -ItemType Directory -Force -Path $out, "$out/classes", "$out/dex" | Out-Null
function CheckExit { if ($LASTEXITCODE -ne 0) { throw "Android fixture command failed: $LASTEXITCODE" } }
& javac -source 8 -target 8 -Xlint:-options -classpath $platform -d "$out/classes" "$PSScriptRoot/ClipboardPeer.java" "$PSScriptRoot/PeerProvider.java"
CheckExit
$classes = @(Get-ChildItem -LiteralPath "$out/classes/com/richedit/clipboardpeer" -Filter '*.class' | ForEach-Object FullName)
& "$buildTools/d8.bat" --min-api 26 --lib $platform --output "$out/dex" @classes
CheckExit
& "$buildTools/aapt2.exe" link --manifest "$PSScriptRoot/AndroidManifest.xml" -I $platform -o "$out/unsigned.apk"
CheckExit
& jar uf "$out/unsigned.apk" -C "$out/dex" classes.dex
CheckExit
& "$buildTools/zipalign.exe" -f 4 "$out/unsigned.apk" "$out/peer.apk"
CheckExit
if (-not (Test-Path -LiteralPath "$out/test.keystore")) {
    & keytool -genkeypair -keystore "$out/test.keystore" -alias test -keyalg RSA -keysize 2048 -validity 3650 -dname 'CN=RichEdit Clipboard Test' -storepass android -keypass android
    CheckExit
}
& "$buildTools/apksigner.bat" sign --ks "$out/test.keystore" --ks-key-alias test --ks-pass pass:android --key-pass pass:android "$out/peer.apk"
CheckExit
Write-Output "$out/peer.apk"
