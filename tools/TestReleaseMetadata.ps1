[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Read-RepoFile([string]$RelativePath) {
    return [IO.File]::ReadAllText((Join-Path $repoRoot $RelativePath))
}

function Assert-Contains(
    [string]$Text,
    [string]$Expected,
    [string]$Description) {
    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Description is missing '$Expected'."
    }
}

[xml]$project = Read-RepoFile "src\Captail\Captail.csproj"
$version = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Captail.csproj has an invalid semantic version: '$version'."
}

$changelog = Read-RepoFile "CHANGELOG.md"
$latestRelease = [regex]::Match(
    $changelog,
    '(?m)^## \[(\d+\.\d+\.\d+)\]')
if (-not $latestRelease.Success -or $latestRelease.Groups[1].Value -ne $version) {
    throw "Project version $version does not match the latest changelog release."
}

$readme = Read-RepoFile "README.md"
Assert-Contains $readme "Captail ``v$version`` is an early public preview" `
    "README preview version"
Assert-Contains $readme "https://github.com/imadraude/captail/releases/latest" `
    "README download link"

$site = Read-RepoFile "site\index.html"
Assert-Contains $site ('"softwareVersion": "' + $version + '"') `
    "Site structured-data version"
Assert-Contains $site "releases/download/v$version/Captail-$version-Setup-win-x64.exe" `
    "Site fallback Setup link"
Assert-Contains $site "releases/download/v$version/Captail-$version-Portable-win-x64.zip" `
    "Site fallback Portable link"

$script = Read-RepoFile "site\script.js"
Assert-Contains $script "api.github.com/repos/imadraude/captail/releases" `
    "Site release lookup"
Assert-Contains $script "Latest verified fallback · V$($version.ToUpperInvariant())" `
    "Site fallback label"

$canonicalFiles = @(
    "README.md",
    "PRIVACY.md",
    ".github\ISSUE_TEMPLATE\config.yml",
    "docs\RELEASING.md",
    "site\index.html",
    "site\script.js",
    "site\robots.txt",
    "site\sitemap.xml",
    "store-listing\listing.json"
)
foreach ($relativePath in $canonicalFiles) {
    if ((Read-RepoFile $relativePath).IndexOf(
            "FaulMit/captail",
            [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "$relativePath still points users to the upstream repository."
    }
}

Write-Host "Release metadata matches Captail $version and imadraude/captail."
