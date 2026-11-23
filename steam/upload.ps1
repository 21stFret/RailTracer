# Builds Red Line for Windows and uploads it to Steam's "internal" branch. Close the Unity editor first.
#   .\steam\upload.ps1                    build + upload (description: the last commit's subject)
#   .\steam\upload.ps1 -Desc "Playtest 3"
#   .\steam\upload.ps1 -SkipBuild         upload the build already in Builds\Steam\Windows
# SteamCMD uses the login cached on this PC (steamcmd +login awkwardwolfstudio once, by hand, to refresh it).
param([string]$Desc, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$steamcmd = 'C:\Users\owner\Documents\steamworks_sdk_165\sdk\tools\ContentBuilder\builder\steamcmd.exe'
$account = 'awkwardwolfstudio'
if (-not $Desc) { $Desc = git -C $root log -1 --format=%s }

if (-not $SkipBuild) {
    $editorOpen = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Replace('/', '\') -match [regex]::Escape($root) -and $_.CommandLine -notmatch 'batchMode' }
    if ($editorOpen) { throw 'Close the Unity editor first: a batch build cannot open a project that is already open.' }
    $version = (Select-String "$root\ProjectSettings\ProjectVersion.txt" -Pattern 'm_EditorVersion: (.+)').Matches[0].Groups[1].Value.Trim()
    $unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    $log = "$root\Builds\Steam\build.log"
    New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
    Write-Host "Building with Unity $version (log: $log)..."
    $p = Start-Process $unity -PassThru -ArgumentList '-batchmode', '-projectPath', "`"$root`"",
        '-executeMethod', 'RailTracer.SteamBuild.Build', '-logFile', "`"$log`""
    $null = $p.Handle # keeps the exit code readable
    $p.WaitForExit() # not -Wait: that also waits on Unity's leftover child processes and never returned
    if ($p.ExitCode -ne 0) { throw "Unity build failed (exit $($p.ExitCode)); see $log" }
}

if (-not (Test-Path "$root\Builds\Steam\Windows\*.exe")) { throw 'No build in Builds\Steam\Windows to upload.' }
Write-Host "Uploading to the internal branch: $Desc"
& $steamcmd +login $account +run_app_build -desc $Desc "$PSScriptRoot\app_build.vdf" +quit
if ($LASTEXITCODE -ne 0) { throw "SteamCMD upload failed (exit $LASTEXITCODE)." }
Write-Host 'Uploaded. Set it live on the default branch by hand in Steamworks when it is ready.'
