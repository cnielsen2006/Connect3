# Drives the Unity editor for this project.
#   -Action Editor   open the project in the editor (press Play yourself)
#   -Action Play     open the editor if needed, then enter play mode in the Boot scene
#   -Action Stop     leave play mode
#   -Action Pause    toggle pause
# A running editor is driven through unity/Library/cascade-command.txt (see Cascade.Editor.EditorCommands); a command written before the
# editor starts runs once it has loaded.
param([ValidateSet('Editor', 'Play', 'Stop', 'Pause')][string]$Action = 'Editor')

$root = Split-Path $PSScriptRoot -Parent
$command = "$root/unity/Library/cascade-command.txt"

# The editor is running if the project's lock file is held by a Unity process.
$running = $false
if (Test-Path "$root/unity/Temp/UnityLockfile") {
    try { [IO.File]::Open("$root/unity/Temp/UnityLockfile", 'Open', 'ReadWrite', 'None').Close() }
    catch { $running = $true }
}

if ($Action -ne 'Editor') {
    if (-not $running -and $Action -ne 'Play') { throw "No Unity editor has this project open; nothing to $Action." }
    New-Item -ItemType Directory -Force (Split-Path $command) | Out-Null
    Set-Content -Path $command -Value $Action.ToLower() -NoNewline
    if ($running) { Write-Host "Sent '$Action' to the running editor."; return }
}
elseif ($running) { Write-Host 'The editor already has this project open.'; return }

$version = (Select-String -Path "$root/unity/ProjectSettings/ProjectVersion.txt" -Pattern 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
$unity = if ($env:UNITY_EXE) { $env:UNITY_EXE } else { "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (-not (Test-Path $unity)) { throw "Unity $version not found at $unity (set UNITY_EXE)" }
Start-Process -FilePath $unity -ArgumentList @('-projectPath', "$root/unity")
$note = if ($Action -eq 'Play') { ' (it will enter play mode once loaded)' } else { '' }
Write-Host "Started Unity $version on unity/$note"
