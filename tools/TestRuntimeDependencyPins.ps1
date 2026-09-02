[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ffmpegScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "tools\AcquireFfmpegRuntime.ps1"))

foreach ($required in @(
    '$version = "n8.1-2026-09-01"',
    '332ec4a9b24064177e0c35fb15eef57afcc52cfd5ddf6cef5126e1e1d4dfa18c',
    '7f5830a562038d561626e583192bf00d52f7ab2b2f7eaaddf3f81bb76791e167'
)) {
    if ($ffmpegScript.IndexOf($required, [StringComparison]::Ordinal) -lt 0) {
        throw "FFmpeg acquisition is missing pinned value: $required"
    }
}

if ($ffmpegScript.IndexOf(
        '$expectedArchiveSha256 = $Matches[1]',
        [StringComparison]::Ordinal) -ge 0) {
    throw "FFmpeg acquisition trusts a mutable release digest at build time."
}

Write-Host "FFmpeg shared and Store-static runtimes have immutable SHA-256 pins."
