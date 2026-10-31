<# Builds the Windows x64 PCVR player (OpenXR) in batch mode.
   Usage:  .\Tools\Windows\build_pcvr.ps1 [-Development] [-OutPath Builds\Windows\VRShootingRange.exe]
   Equivalent in the editor: menu  VR Range > Build > Build Windows PCVR (x64).
   Requires the 'Windows Build Support (Mono)' module for the editor. #>
param([switch]$Development, [string]$OutPath = "Builds\Windows\VRShootingRange.exe")
. (Join-Path $PSScriptRoot "common.ps1")
$args = @("-executeMethod", "VRRange.Editor.PcvrBuildScript.BuildWindowsBatch", "-outPath", $OutPath, "-buildTarget", "Win64")
if ($Development) { $args += "-development" }
$code = Invoke-Unity -Arguments $args -LogName "build_pcvr.log"
$report = Join-Path $ProjectRoot "Logs\VRRangeBuildReport_Windows.txt"
if (Test-Path $report) { Write-Host "`n--- Build report ---"; Get-Content $report }
$full = Join-Path $ProjectRoot $OutPath
if (Test-Path $full) { Write-Host "`nPlayer: $full"; Write-Host "Start your OpenXR runtime (Virtual Desktop Streamer with VDXR, or SteamVR) and run the exe." } else { Write-Warning "Executable not found at $full" }
exit $code
