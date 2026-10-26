<# Streams Unity + VR-Range log lines from the headset to the console and to Logs\quest_logcat.txt.
   Usage:  .\Tools\Windows\logcat.ps1 [-All]   (Ctrl+C to stop) #>
param([switch]$All)
. (Join-Path $PSScriptRoot "common.ps1")
$adb = Find-Adb
$logDir = Join-Path $ProjectRoot "Logs"; New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$out = Join-Path $logDir "quest_logcat.txt"
& $adb logcat -c
Write-Host "Writing to $out (Ctrl+C to stop)"
if ($All) { & $adb logcat | Tee-Object -FilePath $out }
else { & $adb logcat -s Unity:V VRRange:V OpenXR:V OVRPlugin:W AndroidRuntime:E DEBUG:E | Tee-Object -FilePath $out }
