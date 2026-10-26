<# Runs the project's full editor setup (settings, URP, OpenXR, prefabs, scene) in batch mode.
   Usage (PowerShell, from anywhere):  .\Tools\Windows\run_setup.ps1 [-Force]
   Equivalent in the editor: menu  VR Range > Setup > Run Full Setup. #>
param([switch]$Force)
. (Join-Path $PSScriptRoot "common.ps1")
$args = @("-executeMethod", "VRRange.Editor.RangeProjectBootstrap.RunFullSetupBatch")
if ($Force) { $args += "-vrrangeForce" }
$code = Invoke-Unity -Arguments $args -LogName "setup_batch.log"
$report = Join-Path $ProjectRoot "Logs\VRRangeSetupReport.md"
if (Test-Path $report) { Write-Host "`n--- Setup report ---"; Get-Content $report }
exit $code
