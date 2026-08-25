[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "0.1.10",

    [switch]$SkipBuild,

    [string]$ArtifactRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$targetRoot = [IO.Path]::GetFullPath("D:\Captail-0.1.10")
$backupRoot = [IO.Path]::GetFullPath("D:\Captail-0.1.10.__previous")
if (-not $ArtifactRoot) {
    $ArtifactRoot = Join-Path $repoRoot "artifacts\test\manual-$Version"
}
$artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$sourceRoot = Join-Path $artifactRoot "staging\Captail-$Version"

if ($targetRoot -cne "D:\Captail-0.1.10" -or
    $backupRoot -cne "D:\Captail-0.1.10.__previous") {
    throw "Unexpected test deployment path."
}

if (-not $artifactRoot.StartsWith(
        $repoRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Test artifacts must stay inside repository."
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "BuildRelease.ps1") `
        -Version $Version `
        -OutputDirectory $artifactRoot `
        -SkipInstaller
}

$sourceExe = Join-Path $sourceRoot "Captail.exe"
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Published test build not found: $sourceExe"
}

$runningFromTarget = @(
    Get-Process Captail -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Path -and $_.Path.StartsWith(
                $targetRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)
        }
)
if ($runningFromTarget.Count -gt 0) {
    & (Join-Path $targetRoot "Captail.exe") --shutdown-existing
    $shutdownDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $stillRunning = @(
            Get-Process Captail -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Path -and $_.Path.StartsWith(
                        $targetRoot + [IO.Path]::DirectorySeparatorChar,
                        [StringComparison]::OrdinalIgnoreCase)
                }
        )
    } while ($stillRunning.Count -gt 0 -and
        [DateTime]::UtcNow -lt $shutdownDeadline)
    if ($stillRunning.Count -gt 0) {
        throw "Test Captail instance is still running."
    }
}

if (Test-Path -LiteralPath $backupRoot) {
    Remove-Item -LiteralPath $backupRoot -Recurse -Force
}
if (Test-Path -LiteralPath $targetRoot) {
    Move-Item -LiteralPath $targetRoot -Destination $backupRoot
}

try {
    New-Item -ItemType Directory -Path $targetRoot | Out-Null
    Copy-Item -Path (Join-Path $sourceRoot "*") `
        -Destination $targetRoot `
        -Recurse `
        -Force

    foreach ($required in @(
        "Captail.exe",
        "CaptailObsBridge.dll",
        "obs.dll",
        "libmpv-2.dll",
        "ffmpeg\ffmpeg.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $targetRoot $required))) {
            throw "Missing deployed file: $required"
        }
    }
}
catch {
    if (Test-Path -LiteralPath $targetRoot) {
        Remove-Item -LiteralPath $targetRoot -Recurse -Force
    }
    if (Test-Path -LiteralPath $backupRoot) {
        Move-Item -LiteralPath $backupRoot -Destination $targetRoot
    }
    throw
}

if (Test-Path -LiteralPath $backupRoot) {
    Remove-Item -LiteralPath $backupRoot -Recurse -Force
}

$exeInfo = (Get-Item -LiteralPath (Join-Path $targetRoot "Captail.exe")).VersionInfo
$files = @(Get-ChildItem -LiteralPath $targetRoot -Recurse -File)
$sizeBytes = ($files | Measure-Object Length -Sum).Sum

Write-Host ""
Write-Host "Test build deployed: $targetRoot"
[pscustomobject]@{
    ProductVersion = $exeInfo.ProductVersion
    FileVersion = $exeInfo.FileVersion
    Files = $files.Count
    SizeMB = [math]::Round($sizeBytes / 1MB, 1)
}
