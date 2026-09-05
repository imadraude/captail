[CmdletBinding()]
param(
    # Launch directly into the clip editor window for fast trim/preview verification
    [switch]$ClipEditor,

    # Specific clip path to open in the editor (if omitted with -ClipEditor, opens the newest clip)
    [string]$ClipPath = "",

    # Full restart: shuts down running Captail instance and launches full application with recording
    [switch]$FullRestart,

    # Skip the quick incremental build (run existing binary immediately)
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\Captail\Captail.csproj"
$exePath = Join-Path $repoRoot "src\Captail\bin\Debug\net9.0-windows10.0.22621.0\win-x64\Captail.exe"

if (-not $NoBuild) {
    Write-Host "Compiling incremental managed changes..." -ForegroundColor Cyan
    & dotnet build $projectPath -c Debug -p:BuildNativeDependencies=false --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Incremental build failed."
    }
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Executable not found at: $exePath"
}

if ($ClipEditor) {
    if (-not $ClipPath) {
        $configPath = "$env:APPDATA\Captail\config.json"
        $searchDirs = @()
        if (Test-Path -LiteralPath $configPath) {
            try {
                $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
                if ($cfg.OutputDirectory -and (Test-Path -LiteralPath $cfg.OutputDirectory)) {
                    $searchDirs += $cfg.OutputDirectory
                }
            }
            catch {}
        }
        $searchDirs += "$env:USERPROFILE\Videos\Captail"

        $latestClip = $null
        foreach ($dir in $searchDirs) {
            if (Test-Path -LiteralPath $dir) {
                $candidate = Get-ChildItem -LiteralPath $dir -Recurse -File -Include *.mp4, *.mkv |
                    Sort-Object LastWriteTime -Descending |
                    Select-Object -First 1
                if ($candidate -and (-not $latestClip -or $candidate.LastWriteTime -gt $latestClip.LastWriteTime)) {
                    $latestClip = $candidate
                }
            }
        }

        if ($latestClip) {
            $ClipPath = $latestClip.FullName
            Write-Host "Selected newest replay: $ClipPath" -ForegroundColor Green
        }
        else {
            throw "No replay clips found to test. Please specify -ClipPath <path-to-video>."
        }
    }

    Write-Host "Launching Clip Editor test session for: $ClipPath" -ForegroundColor Cyan
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -ArgumentList "`"--qa-clip-editor=$ClipPath`""
    return
}

if ($FullRestart) {
    Write-Host "Shutting down existing Captail instances..." -ForegroundColor Yellow
    $running = Get-Process Captail -ErrorAction SilentlyContinue
    if ($running) {
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }

    Write-Host "Launching full Captail test session..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath)
    return
}

# Default: UI-only mode (runs in isolated instance, doesn't interfere with background tray instance)
Write-Host "Launching isolated UI test session (--ui-only)..." -ForegroundColor Cyan
Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -ArgumentList "--ui-only"
