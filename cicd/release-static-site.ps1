<#
.SYNOPSIS
    Builds a signed release for a static site managed by the self-update system
    (see OrbitMesh.Server/Services/StaticSiteUpdater.cs) - the Console, or any other.

.DESCRIPTION
    No compilation step (unlike release-server.ps1) - these are just files, copied as-is:
    1. Reads/bumps a VERSION file at the root of -SourceDir (the static-site equivalent of the
       Server csproj's <Version> - there's no assembly to read a version from).
    2. Copies -SourceDir into a staging folder, skipping -ExcludeFiles (e.g. a site's own
       config.json, which might hold a specific device's server URL/access key - never something
       to ship in a release) and the VERSION file itself (an authoring artifact, not something
       the deployed site needs - StaticSiteUpdater.ApplyAsync tracks the deployed version in its
       own separate marker file).
    3. Hashes every remaining file (SHA-256) into manifest.json, same shape as release-server.ps1.
    4. Signs it with -PrivateKeyPath (RS256 via openssl), same as release-server.ps1. Skipped if
       no key is given.
    5. Zips manifest.json (+ .sig) with the files, flat - what
       StaticSiteUpdater.ApplyAsync expects at the root of the release.zip.

.EXAMPLE
    .\release-static-site.ps1 -SourceDir D:\Dev\repos\OrbitMesh\src\orbitmesh-console -Slug orbitmesh-console -Version 1.1.0 -PrivateKeyPath D:\keys\release-signing.key

.EXAMPLE
    .\release-static-site.ps1 -SourceDir D:\Dev\repos\OrbitMesh\my-dashboard -Slug my-dashboard -Version 1.0.0 -ExcludeFiles config.json
#>
param(
    [Parameter(Mandatory)][string]$SourceDir,
    [Parameter(Mandatory)][string]$Slug,
    [string]$Version,
    [string[]]$ExcludeFiles = @(),
    [string]$PrivateKeyPath,
    [string]$Passphrase,
    [string]$OutputDir = "$PSScriptRoot\build"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\ReleaseHelpers.ps1"

$SourceDir = (Resolve-Path $SourceDir).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

# --- 1. Version -------------------------------------------------------------

$versionFile = Join-Path $SourceDir "VERSION"
if ($Version) {
    Write-Step "Setting VERSION to $Version"
    [System.IO.File]::WriteAllText($versionFile, $Version, (New-Object System.Text.UTF8Encoding($false)))
} else {
    if (-not (Test-Path $versionFile)) {
        throw "No VERSION file found in $SourceDir and none was given via -Version."
    }
    $Version = (Get-Content $versionFile -Raw).Trim()
}
Write-Host "Release version: $Version"

# --- 2. Stage files (excluding VERSION and -ExcludeFiles) -------------------

$stagingDir = Join-Path $OutputDir "$Slug-staging"
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

$excludeNormalized = (@("VERSION") + $ExcludeFiles) | ForEach-Object { $_ -replace '\\', '/' }

Write-Step "Staging files from $SourceDir"
$copied = 0
Get-ChildItem $SourceDir -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($SourceDir.Length + 1) -replace '\\', '/'
    if ($excludeNormalized -contains $relative) {
        Write-Host "Excluded: $relative" -ForegroundColor Yellow
        return
    }
    $destination = Join-Path $stagingDir $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    Copy-Item $_.FullName $destination
    $copied++
}
if ($copied -eq 0) {
    throw "No files staged from $SourceDir - check -ExcludeFiles isn't excluding everything."
}
Write-Host "$copied files staged"

# --- 3. Manifest ---------------------------------------------------------------

Write-Step "Hashing staged files"
$files = Get-FileHashesRecursive -Root $stagingDir
Write-Host "$($files.Count) files hashed"

$manifestPath = Join-Path $stagingDir "manifest.json"
New-ReleaseManifest -Version $Version -Roots @(".") -Files $files -OutFile $manifestPath | Out-Null
Write-Host "manifest.json written ($((Get-Item $manifestPath).Length) bytes)"

# --- 4. Sign ---------------------------------------------------------------

$sigPath = $null
if ($PrivateKeyPath) {
    Write-Step "Signing manifest.json (RS256)"
    $sigPath = New-ReleaseSignature -ManifestPath $manifestPath -PrivateKeyPath $PrivateKeyPath -Passphrase $Passphrase
    Write-Host "Signed: $sigPath"
} else {
    Write-Host "No -PrivateKeyPath given - release will be unsigned." -ForegroundColor Yellow
}

# --- 5. Zip ---------------------------------------------------------------

Write-Step "Creating release archive"
$zipName = "$Slug-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName
New-ZipArchive -SourceDir $stagingDir -DestinationZip $zipPath

Copy-Item $manifestPath (Join-Path $OutputDir "manifest.json") -Force
if ($sigPath) {
    Copy-Item $sigPath (Join-Path $OutputDir "manifest.json.sig") -Force
}

Write-Host ""
Write-Host "Release built successfully." -ForegroundColor Green
Write-Host "Slug    : $Slug"
Write-Host "Version : $Version"
Write-Host "Files   : $($files.Count)"
Write-Host "Archive : $zipPath"
Write-Host "Signed  : $(if ($sigPath) { 'yes' } else { 'no' })"
Write-Host ""
Write-Host "Upload $zipName to your ci4-updater-server '$Slug' project as the new release."
