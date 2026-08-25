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

$app = Read-Text "src\Captail\App.xaml.cs"
$indicator = Read-Text "src\Captail\ReplayStatusIndicatorWindow.xaml.cs"
$settings = Read-Text "src\Captail\SettingsWindow.xaml"

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
Assert-Contains $settings `
    'Icon="pack://application:,,,/Captail;component/Assets/Captail\.ico"' `
    "Main window must use an absolute pack URI for its taskbar icon."

Write-Host "STARTUP_UI_SURFACES_TEST PASS"
