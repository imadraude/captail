[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Read-Text([string]$relativePath) {
    return [IO.File]::ReadAllText((Join-Path $repoRoot $relativePath))
}

function Assert-Contains(
    [string]$text,
    [string]$pattern,
    [string]$message
) {
    if ($text -notmatch $pattern) {
        throw $message
    }
}

function Assert-IcoFrames([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 6) {
        throw "$relativePath is too short to contain an ICO header."
    }

    $expectedSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frameCount = [BitConverter]::ToUInt16($bytes, 4)
    if ($frameCount -ne $expectedSizes.Count) {
        throw "$relativePath contains $frameCount frames; expected $($expectedSizes.Count)."
    }

    $directoryLength = 6 + 16 * $frameCount
    if ($bytes.Length -lt $directoryLength) {
        throw "$relativePath is shorter than its ICO directory."
    }

    for ($index = 0; $index -lt $frameCount; $index++) {
        $entryOffset = 6 + 16 * $index
        $width = if ($bytes[$entryOffset] -eq 0) { 256 } else { [int]$bytes[$entryOffset] }
        $height = if ($bytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$bytes[$entryOffset + 1] }
        $frameLength = [BitConverter]::ToUInt32($bytes, $entryOffset + 8)
        $frameOffset = [BitConverter]::ToUInt32($bytes, $entryOffset + 12)
        $frameEnd = [uint64]$frameOffset + [uint64]$frameLength
        $expectedSize = $expectedSizes[$index]

        if ($width -ne $expectedSize -or $height -ne $expectedSize) {
            throw "$relativePath frame $index is ${width}x${height}; expected ${expectedSize}x${expectedSize}."
        }
        if ($frameOffset -lt $directoryLength -or $frameEnd -gt $bytes.Length) {
            throw "$relativePath frame $index (${expectedSize}x${expectedSize}) points outside the file."
        }
    }
}

$app = Read-Text "src\Captail\App.xaml.cs"
$indicator = Read-Text "src\Captail\ReplayStatusIndicatorWindow.xaml.cs"
$settings = Read-Text "src\Captail\SettingsWindow.xaml"
$settingsCode = Read-Text "src\Captail\SettingsWindow.xaml.cs"

Assert-IcoFrames "src\Captail\Assets\Captail.ico"
Assert-IcoFrames "src\Captail\Assets\CaptailInactive.ico"

Assert-Contains $app `
    '_tray\.ForceCreate\(enablesEfficiencyMode:\s*false\)' `
    "Background recording must not enable H.NotifyIcon Efficiency Mode."
Assert-Contains $app `
    'SetCurrentProcessExplicitAppUserModelID' `
    "Portable builds need a stable shell identity after sign-in."
Assert-Contains $indicator `
    'ContentRendered\s*\+=' `
    "Recording indicator capture protection must wait for its first frame."
Assert-Contains $indicator `
    'if\s*\(!_firstFrameRendered\)\s*return;' `
    "Recording indicator must not apply capture affinity before rendering."
Assert-Contains $indicator `
    'CompleteFirstFrame\(\)[\s\S]*?ResetLastNativeBounds\(\);[\s\S]*?PositionOnForegroundMonitor\(\);' `
    "Recording indicator must reapply native bounds after its first rendered frame."
Assert-Contains $settings `
    'Icon="pack://application:,,,/Captail;component/Assets/Captail\.ico"' `
    "Main window must use an absolute pack URI for its taskbar icon."
Assert-Contains $settingsCode `
    'UpdateSourceStatusDot\(SystemSourceChip,\s*SystemSourceDot\);' `
    "Runtime refreshes must preserve the system source dot visual state."
Assert-Contains $settingsCode `
    'UpdateSourceStatusDot\(MicSourceChip,\s*MicSourceDot\);' `
    "Runtime refreshes must preserve the microphone source dot visual state."

Write-Host "STARTUP_UI_SURFACES_TEST PASS"
