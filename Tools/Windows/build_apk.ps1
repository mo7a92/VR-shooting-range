<# Builds the Meta Quest 2 APK in batch mode.
   Usage:  .\Tools\Windows\build_apk.ps1 [-Development] [-BakeLighting] [-ApkPath Builds\VRShootingRange.apk]
   Equivalent in the editor: menu  VR Range > Build > Build Quest 2 APK. #>
param([switch]$Development, [switch]$BakeLighting, [string]$ApkPath = "Builds\VRShootingRange.apk")
. (Join-Path $PSScriptRoot "common.ps1")
$args = @("-executeMethod", "VRRange.Editor.QuestBuildScript.BuildApkBatch", "-apkPath", $ApkPath, "-buildTarget", "Android")
if ($Development) { $args += "-development" }
if ($BakeLighting) { $args += "-bakeLighting" }
$code = Invoke-Unity -Arguments $args -LogName "build_apk.log"
$report = Join-Path $ProjectRoot "Logs\VRRangeBuildReport.txt"
if (Test-Path $report) { Write-Host "`n--- Build report ---"; Get-Content $report }
$full = Join-Path $ProjectRoot $ApkPath
if (Test-Path $full) { Write-Host "`nAPK: $full ($([math]::Round((Get-Item $full).Length / 1MB)) MB)" } else { Write-Warning "APK not found at $full" }
exit $code
