<# Installs the APK on a connected Meta Quest 2 (Developer Mode + USB debugging authorised) and launches it.
   Usage:  .\Tools\Windows\install_apk.ps1 [-ApkPath Builds\VRShootingRange.apk] [-NoLaunch] #>
param([string]$ApkPath = "Builds\VRShootingRange.apk", [switch]$NoLaunch)
. (Join-Path $PSScriptRoot "common.ps1")
$adb = Find-Adb
$full = Join-Path $ProjectRoot $ApkPath
if (-not (Test-Path $full)) { throw "APK not found: $full  (run build_apk.ps1 first)" }
Write-Host "adb: $adb"
& $adb devices
$devices = (& $adb devices) | Select-String -Pattern "`tdevice$"
if (-not $devices) { throw "No authorised device. Put on the headset and accept 'Allow USB debugging', then retry. If it says 'unauthorized', revoke/accept again." }
Write-Host "Installing $full ..."
& $adb install -r -g $full
if ($LASTEXITCODE -ne 0) { throw "adb install failed" }
if (-not $NoLaunch) {
    $pkg = "com.universityproject.vrshootingrange"
    Write-Host "Launching $pkg ..."
    & $adb shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 | Out-Null
    Write-Host "Launched. Put on the headset. Capture logs with .\Tools\Windows\logcat.ps1"
}
