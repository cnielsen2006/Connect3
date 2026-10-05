# Builds the dev APK (Unity batch mode), installs it with adb and starts it on a connected phone or the emulator.
#   -Target device     the one USB-connected device (adb -d)
#   -Target emulator   the AVD (adb -e); started if none is running
#   Builds in the open Unity editor if there is one (no need to close it), else in batch mode.
#   -SkipBuild         install and run the newest APK in Builds/Android
param(
    [Parameter(Mandatory)][ValidateSet('device', 'emulator')][string]$Target,
    [switch]$SkipBuild,
    [string]$Avd = 'cascade_api36'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$package = 'com.phoenix.cascade.dev'
$activity = "$package/com.unity3d.player.UnityPlayerGameActivity"
$sdk = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" }
$adb = if (Get-Command adb -ErrorAction SilentlyContinue) { 'adb' } else { "$sdk\platform-tools\adb.exe" }
$sel = if ($Target -eq 'device') { '-d' } else { '-e' }

if (-not $SkipBuild) {
    # The editor is running if the project's lock file is held by a Unity process.
    $editorOpen = $false
    $lock = "$root/unity/Temp/UnityLockfile"
    if (Test-Path $lock) {
        try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Close() } catch { $editorOpen = $true }
    }

    if ($editorOpen) {
        # Have the open editor build (Cascade.Editor.EditorCommands); no need to close it.
        Write-Host 'Building the Android dev APK in the running Unity editor...'
        $command = "$root/unity/Library/cascade-command.txt"
        $result = "$root/unity/Library/cascade-build.txt"
        Remove-Item $result -ErrorAction SilentlyContinue
        Set-Content -Path $command -Value 'build-android' -NoNewline
        $deadline = (Get-Date).AddMinutes(30)
        while (-not (Test-Path $result)) {
            if ((Get-Date) -gt $deadline) { throw 'Timed out waiting for the editor to finish the build; see the Unity Console.' }
            Start-Sleep -Seconds 2
        }
        if ((Get-Content $result -Raw).Trim() -ne 'ok') { throw 'Unity build failed; see the Unity Console (or unity/Logs/Editor.log).' }
    } else {
        $version = (Select-String -Path "$root/unity/ProjectSettings/ProjectVersion.txt" -Pattern 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
        $unity = if ($env:UNITY_EXE) { $env:UNITY_EXE } else { "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
        if (-not (Test-Path $unity)) { throw "Unity $version not found at $unity (set UNITY_EXE)" }
        Write-Host 'Building the Android dev APK (no editor open; using batch mode)...'
        & $unity -batchmode -nographics -quit -projectPath "$root/unity" -buildTarget Android `
            -executeMethod Cascade.Editor.BuildScript.BuildAndroid -dev -logFile "$root/Builds/build.log" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Unity build failed (exit $LASTEXITCODE); see Builds/build.log" }
    }
    & $adb kill-server | Out-Null   # Unity restarts its own adb daemon, which leaves devices offline (TRD 13.10)
    & $adb start-server | Out-Null
}

$apk = Get-ChildItem "$root/Builds/Android/*-dev.apk" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw 'No dev APK in Builds/Android; run without -SkipBuild.' }

if ($Target -eq 'emulator') {
    $running = (& $adb devices) -match '^emulator-\d+\s+device'
    if (-not $running) {
        Write-Host "Starting emulator $Avd..."
        Start-Process "$sdk\emulator\emulator.exe" -ArgumentList @('-avd', $Avd, '-no-snapshot', '-no-audio')
    }
    & $adb -e wait-for-device
    while ((& $adb -e shell getprop sys.boot_completed).Trim() -ne '1') { Start-Sleep -Seconds 2 }
} else {
    if (-not ((& $adb devices) -match '^\S+\s+device$' | Where-Object { $_ -notmatch '^emulator-' })) {
        throw 'No USB device connected (adb devices shows none). Enable USB debugging and authorize this PC.'
    }
}

Write-Host "Installing $($apk.Name) on the $Target..."
& $adb $sel install -r $apk.FullName
if ($LASTEXITCODE -ne 0) { throw 'adb install failed' }

# Vulkan segfaults under the emulator's ARM translation, so the emulator runs GLES (TRD 13.10).
$extra = if ($Target -eq 'emulator') { @('-e', 'unity', '-force-gles') } else { @() }
& $adb $sel shell am force-stop $package
& $adb $sel shell am start -n $activity @extra
Write-Host "Running. Logs: adb $sel logcat -s Unity"
