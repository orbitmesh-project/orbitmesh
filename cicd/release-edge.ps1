<#
.SYNOPSIS
    Builds a signed OrbitMesh.Edge release for the self-update system
    (see OrbitMesh.Edge/Services/EdgeSelfUpdater.cs and ci4-updater-server).

.DESCRIPTION
    Same shape as release-server.ps1 (see its own doc comment for the full rationale) - EdgeSelfUpdater
    mirrors ServerSelfUpdater exactly (flat zip, manifest.json/.sig, OrbitMesh.Edge.dll expected at the
    zip's root instead of OrbitMesh.Server.dll):
    1. Optionally bumps <Version> in OrbitMesh.Edge.csproj.
    2. dotnet publish (framework-dependent). Portable by default (no -r) - runs on any
       OS/architecture with a matching installed runtime, same zip for a Raspberry Pi (linux-arm64)
       and an x64 dev box/WSL alike. Pass -Runtime for a RID-specific build instead.
    3. Hashes every published file (SHA-256) into manifest.json.
    4. Signs manifest.json with -PrivateKeyPath (RS256, via openssl). Skipped if no key is given.
    5. Zips manifest.json (+ .sig) and the published files together, flat.

    Upload the resulting .zip as a new release for the "orbitmesh-edge" project.

.EXAMPLE
    .\release-edge.ps1 -Version 1.1.0 -PrivateKeyPath D:\keys\release-signing.key

.EXAMPLE
    .\release-edge.ps1
    # Uses whatever <Version> is currently in the csproj, unsigned, portable (any Linux architecture).
#>
param(
    [string]$Version,
    [string]$PrivateKeyPath,
    [string]$Passphrase,
    [string]$Runtime = "",
    [string]$ProjectPath = "$PSScriptRoot\..\src\orbitmesh-server\OrbitMesh.Edge\OrbitMesh.Edge.csproj",
    [string]$OutputDir = "$PSScriptRoot\build"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\ReleaseHelpers.ps1"

$ProjectPath = (Resolve-Path $ProjectPath).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

# --- 1. Version -------------------------------------------------------------

[xml]$csproj = Get-Content $ProjectPath
$versionNode = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
if ($Version) {
    Write-Step "Setting <Version> to $Version in $(Split-Path -Leaf $ProjectPath)"
    ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version = $Version
    $csproj.Save($ProjectPath)
} else {
    $Version = $versionNode
    if (-not $Version) {
        throw "No <Version> found in $ProjectPath and none was given via -Version."
    }
}
Write-Host "Release version: $Version"

# --- 2. Publish ---------------------------------------------------------------

$publishDir = Join-Path $OutputDir "edge-publish"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Step "dotnet publish ($(if ($Runtime) { $Runtime } else { 'portable, any architecture' }), framework-dependent)"
if ($Runtime) {
    dotnet publish $ProjectPath -c Release -r $Runtime --self-contained false -o $publishDir
} else {
    dotnet publish $ProjectPath -c Release -o $publishDir
}
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path (Join-Path $publishDir "OrbitMesh.Edge.dll"))) {
    throw "OrbitMesh.Edge.dll not found in publish output - refusing to package this."
}

# The SDK bundles this machine's own appsettings*.json into the publish output - strip it, a release
# should never carry real credentials.
Get-ChildItem $publishDir -Filter "appsettings*.json" | ForEach-Object {
    Write-Host "Removing $($_.Name) from the publish output (never part of a release)" -ForegroundColor Yellow
    Remove-Item $_.FullName -Force
}

# --- 3. Manifest ---------------------------------------------------------------

Write-Step "Hashing published files"
$files = Get-FileHashesRecursive -Root $publishDir
Write-Host "$($files.Count) files hashed"

$manifestPath = Join-Path $publishDir "manifest.json"
# Informational only - EdgeSelfUpdater.ApplyAsync replaces the whole app directory regardless of this
# value; it doesn't do ci4-updater's per-root scanning/scoping.
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
$zipName = "orbitmesh-edge-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName
New-ZipArchive -SourceDir $publishDir -DestinationZip $zipPath

Copy-Item $manifestPath (Join-Path $OutputDir "manifest.json") -Force
if ($sigPath) {
    Copy-Item $sigPath (Join-Path $OutputDir "manifest.json.sig") -Force
}

Write-Host ""
Write-Host "Release built successfully." -ForegroundColor Green
Write-Host "Version : $Version"
Write-Host "Files   : $($files.Count)"
Write-Host "Archive : $zipPath"
Write-Host "Signed  : $(if ($sigPath) { 'yes' } else { 'no' })"
Write-Host ""
Write-Host "Upload $zipName to your ci4-updater-server 'orbitmesh-edge' project as the new release."
