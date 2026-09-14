[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ffmpegScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "tools\AcquireFfmpegRuntime.ps1"))

foreach ($required in @(
    '$version = "n8.1-2026-09-14"',
    '398be5fb6e09ff3ae419ad62436d566c62705b1cc59bd4f98e8cd79646d42708',
    '72aed4497d242b5456fc81e924310875727805a40d1d7ad5ff4cb9dbb41558fb',
    'autobuild-2026-09-14-13-17'
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
