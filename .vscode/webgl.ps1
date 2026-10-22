# Builds the WebGL player and serves it on the LAN, so a phone or another PC on the same network can open it.
#   -Action Build        build into Builds/WebGL/dev (or /release with -Release)
#   -Action Serve        serve the newest build on every network interface; prints http://<this PC>:28420/ addresses
#   -Action BuildServe   build, then serve
#   -Action Run          serve, building first only if there is no build yet (what the desktop shortcut uses)
#   -Release             the release build (gzip, no dev tools) instead of the dev build (three-finger tap opens the dev page)
#   -Port <n>            default 28420
#   Builds in the open Unity editor if there is one (no need to close it), else in batch mode.
#   The server is Cascade.Tools serve (tools/Cascade.Tools/ServeCommand.cs); Ctrl+C stops it.
param(
    [Parameter(Mandatory)][ValidateSet('Build', 'Serve', 'BuildServe', 'Run')][string]$Action,
    [switch]$Release,
    [int]$Port = 28420
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$variant = if ($Release) { 'release' } else { 'dev' }
$folder = "$root/Builds/WebGL/$variant"

function Build-WebGL {
    # The editor is running if the project's lock file is held by a Unity process.
    $editorOpen = $false
    $lock = "$root/unity/Temp/UnityLockfile"
    if (Test-Path $lock) {
        try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Close() } catch { $editorOpen = $true }
    }

    if ($editorOpen) {
        # Have the open editor build (Cascade.Editor.EditorCommands); no need to close it.
        Write-Host "Building the WebGL $variant build in the running Unity editor (the first WebGL build switches the platform and is slow)..."
        $command = "$root/unity/Library/cascade-command.txt"
        $result = "$root/unity/Library/cascade-build.txt"
        Remove-Item $result -ErrorAction SilentlyContinue
        Set-Content -Path $command -Value $(if ($Release) { 'build-webgl-release' } else { 'build-webgl' }) -NoNewline
        $deadline = (Get-Date).AddMinutes(45)
        while (-not (Test-Path $result)) {
            if ((Get-Date) -gt $deadline) { throw 'Timed out waiting for the editor to finish the build; see the Unity Console.' }
            Start-Sleep -Seconds 2
        }
        if ((Get-Content $result -Raw).Trim() -ne 'ok') { throw 'Unity build failed; see the Unity Console (or unity/Logs/Editor.log).' }
    } else {
        $version = (Select-String -Path "$root/unity/ProjectSettings/ProjectVersion.txt" -Pattern 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
        $unity = if ($env:UNITY_EXE) { $env:UNITY_EXE } else { "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
        if (-not (Test-Path $unity)) { throw "Unity $version not found at $unity (set UNITY_EXE)" }
        Write-Host "Building the WebGL $variant build (no editor open; using batch mode, this takes several minutes)..."
        New-Item -ItemType Directory -Force "$root/Builds" | Out-Null
        $unityArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', "$root/unity", '-buildTarget', 'WebGL',
            '-executeMethod', 'Cascade.Editor.BuildScript.BuildWebGL', '-logFile', "$root/Builds/build-webgl.log")
        if (-not $Release) { $unityArgs += '-dev' }
        & $unity @unityArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Unity build failed (exit $LASTEXITCODE); see Builds/build-webgl.log" }
    }
    Write-Host "Built $folder"
}

if ($Action -in 'Build', 'BuildServe' -or ($Action -eq 'Run' -and -not (Test-Path "$folder/index.html"))) { Build-WebGL }

if ($Action -in 'Serve', 'BuildServe', 'Run') {
    if (-not (Test-Path "$folder/index.html")) { throw "No $variant WebGL build in $folder; run Build first." }
    dotnet run --project "$root/tools/Cascade.Tools" -- serve $folder --port $Port
}
