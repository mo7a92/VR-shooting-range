# Shared helpers for the VR Shooting Range Windows scripts. Dot-source from the other scripts.
$ErrorActionPreference = "Stop"
$script:ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Get-UnityVersion {
    $line = Get-Content (Join-Path $script:ProjectRoot "ProjectSettings\ProjectVersion.txt") | Where-Object { $_ -like "m_EditorVersion:*" }
    return ($line -split ":\s*")[1].Trim()
}

function Find-UnityEditor {
    param([string]$Version)
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$Version\Editor\Unity.exe",
        "${env:ProgramFiles(x86)}\Unity\Hub\Editor\$Version\Editor\Unity.exe",
        "D:\Unity\Hub\Editor\$Version\Editor\Unity.exe",
        "C:\Unity\Hub\Editor\$Version\Editor\Unity.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    # Any installed 6000.x editor as a fallback (Unity upgrades the project in place; the Hub asks once).
    $hub = "$env:ProgramFiles\Unity\Hub\Editor"
    if (Test-Path $hub) {
        $any = Get-ChildItem $hub -Directory | Where-Object { $_.Name -like "6000.*" } | Sort-Object Name -Descending | Select-Object -First 1
        if ($any) { $exe = Join-Path $any.FullName "Editor\Unity.exe"; if (Test-Path $exe) { Write-Warning "Exact Unity $Version not found; using $($any.Name)"; return $exe } }
    }
    if ($env:UNITY_EDITOR_EXE -and (Test-Path $env:UNITY_EDITOR_EXE)) { return $env:UNITY_EDITOR_EXE }
    throw "Unity Editor $Version not found. Install it with Unity Hub (with Android Build Support) or set UNITY_EDITOR_EXE to Unity.exe."
}

function Find-Adb {
    if ($env:ANDROID_ADB -and (Test-Path $env:ANDROID_ADB)) { return $env:ANDROID_ADB }
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $version = Get-UnityVersion
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
        "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $hub = "$env:ProgramFiles\Unity\Hub\Editor"
    if (Test-Path $hub) {
        $found = Get-ChildItem $hub -Recurse -Filter adb.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw "adb.exe not found. Install Android SDK & NDK Tools for the Unity editor (Unity Hub > Installs > Add modules) or set ANDROID_ADB."
}

function Invoke-Unity {
    param([string[]]$Arguments, [string]$LogName)
    $unity = Find-UnityEditor (Get-UnityVersion)
    $logDir = Join-Path $script:ProjectRoot "Logs"; New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $log = Join-Path $logDir $LogName
    Write-Host "Unity: $unity"
    Write-Host "Log:   $log"
    $all = @("-batchmode", "-nographics", "-projectPath", $script:ProjectRoot, "-logFile", $log) + $Arguments + @("-quit")
    $p = Start-Process -FilePath $unity -ArgumentList $all -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { Write-Warning "Unity exited with code $($p.ExitCode). Last log lines:"; Get-Content $log -Tail 60 }
    return $p.ExitCode
}
