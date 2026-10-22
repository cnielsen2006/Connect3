# Creates a "Cascade (serve on LAN)" shortcut on the Desktop. Double-click it to start the WebGL server
# (building the dev WebGL player first if there is none); the window prints the http://<this PC>:28420/ addresses
# to open on a phone, and closing the window stops the server.
#   -Release   serve the release build instead of the dev build
param([switch]$Release)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$desktop = [Environment]::GetFolderPath('Desktop')
$suffix = if ($Release) { ' -Release' } else { '' }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut("$desktop\Cascade (serve on LAN).lnk")
$link.TargetPath = (Get-Command powershell.exe).Source
# -NoExit keeps the window open so an error (or the server's URLs) stays readable.
$link.Arguments = "-NoProfile -NoExit -ExecutionPolicy Bypass -File `"$PSScriptRoot\webgl.ps1`" -Action Run$suffix"
$link.WorkingDirectory = $root
$link.IconLocation = "$root\tools\icon\cascade.ico"
$link.Description = 'Serve Cascade (WebGL) on the local network'
$link.Save()
Write-Host "Created $desktop\Cascade (serve on LAN).lnk"
