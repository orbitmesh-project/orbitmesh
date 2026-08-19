<#
.SYNOPSIS
    Builds a signed OrbitMesh.Server release for the self-update system
    (see OrbitMesh.Server/Services/ServerSelfUpdater.cs and ci4-updater-server).

.DESCRIPTION
    1. Optionally bumps <Version> in OrbitMesh.Server.csproj.
    2. dotnet publish (framework-dependent). Portable by default (no -r) - runs on any
       OS/architecture with a matching installed runtime, same zip for a Raspberry Pi (linux-arm64)
       and an x64 dev box/WSL alike. Pass -Runtime for a RID-specific build instead (e.g. smaller,
       or if you need R2R/native optimizations for one specific target) - but note that still
       requires a matching architecture at the other end, framework-dependent or not.
    3. Hashes every published file (SHA-256) into manifest.json (version/date/roots/files),
       matching ci4-updater's own manifest shape.
    4. Signs manifest.json with -PrivateKeyPath (RS256, via openssl - PEM import isn't
       available to RSA.ImportFromPem() under Windows PowerShell 5.1's .NET Framework,
       so this shells out to Git's bundled openssl.exe instead). Skipped if no key is given.
    5. Zips manifest.json (+ .sig) and the published files together, flat - this is exactly
       what ServerSelfUpdater.ApplyAsync expects to find at the root of release.zip.

    Upload the resulting .zip (and manifest.json/.sig, if your ci4-updater-server admin form
    wants them as separate fields) as a new release for the "orbitmesh-server" project.

    See also release-static-site.ps1 for Console (or any other static site) releases - same
    manifest/signing shape, but without the dotnet publish step (they're just static files).

.EXAMPLE
    .\release-server.ps1 -Version 1.1.0 -PrivateKeyPath D:\keys\release-signing.key

.EXAMPLE
    .\release-server.ps1
    # Uses whatever <Version> is currently in the csproj, unsigned, portable (any Linux architecture).
#>
param(
    [string]$Version,
    [string]$PrivateKeyPath,
    [string]$Passphrase,
    [string]$Runtime = "",
    [string]$ProjectPath = "$PSScriptRoot\..\src\orbitmesh-server\OrbitMesh.Server\OrbitMesh.Server.csproj",
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

$publishDir = Join-Path $OutputDir "server-publish"
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

if (-not (Test-Path (Join-Path $publishDir "OrbitMesh.Server.dll"))) {
    throw "OrbitMesh.Server.dll not found in publish output - refusing to package this."
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
# Informational only - ServerSelfUpdater.ApplyAsync replaces the whole app directory regardless
# of this value; it doesn't do ci4-updater's per-root scanning/scoping.
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
$zipName = "orbitmesh-server-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName
# manifest.json/.sig already live inside $publishDir (written there above), so one pass over its
# contents zips the release code and the manifest together, flat - exactly what
# ServerSelfUpdater.ApplyAsync extracts and looks for OrbitMesh.Server.dll in.
New-ZipArchive -SourceDir $publishDir -DestinationZip $zipPath

# Standalone copies alongside the zip, in case your ci4-updater-server upload form wants
# manifest.json / manifest.json.sig as separate fields rather than reading them from the zip.
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
Write-Host "Upload $zipName to your ci4-updater-server 'orbitmesh-server' project as the new release."
