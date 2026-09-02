[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ffmpegScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "tools\AcquireFfmpegRuntime.ps1"))

foreach ($required in @(
    '$version = "n8.1-2026-09-01"',
    '14fea72ee692a5f832b8d7b0c7f1c050af124f72cf43d6a948faf98ff3c0072d',
    '7aeceacf1d52f19a9d3eb232a094d8cfe2883dfd0f566e5c00ea84151b146a55',
    'autobuild-2026-09-01-13-13'
)) {
    if ($ffmpegScript.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "FFmpeg acquisition is missing pinned value: $required"
    }
}

if ($ffmpegScript.IndexOf("releases/download/latest", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "FFmpeg acquisition still uses the mutable latest download URL."
}

if ($ffmpegScript.IndexOf(
        '$expectedArchiveSha256 = $Matches[1]',
        [StringComparison]::Ordinal) -ge 0) {
    throw "FFmpeg acquisition trusts a mutable release digest at build time."
}

Write-Host "FFmpeg shared and Store-static runtimes have immutable SHA-256 pins."
