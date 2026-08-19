<#
.SYNOPSIS
    Builds and packages OrbitMesh.Updater for distribution.

.DESCRIPTION
    Same shape as release-server.ps1/release-edge.ps1 (manifest.json + optional signature), even
    though OrbitMesh.Updater never verifies either itself - it's never updated through the
    self-update system it implements (see ServerSelfUpdater/EdgeSelfUpdater), it lives outside the
    "live" directory those updates replace. The manifest is included purely because upload services
    (e.g. Pepite) require one in every release zip.

    Portable by default (no -r) - same zip runs on any architecture with a matching installed
    runtime. Pass -Runtime for a RID-specific build instead.

    Deploy the resulting zip once per host, extracted into a sibling "updater" folder next to the
    Server/Edge install directory (e.g. .../server/../updater/) - or wherever
    UpdateOptions.UpdaterExecutablePath points to instead. Upload it to your update server as the
    "orbitmesh-updater" project too, to have install.sh/install.ps1 fetch it automatically.

.EXAMPLE
    .\release-updater.ps1

.EXAMPLE
    .\release-updater.ps1 -Version 1.1.0 -PrivateKeyPath D:\keys\release-signing.key
#>
param(
    [string]$Version,
    [string]$PrivateKeyPath,
    [string]$Passphrase,
    [string]$Runtime = "",
    [string]$ProjectPath = "$PSScriptRoot\..\src\orbitmesh-server\OrbitMesh.Updater\OrbitMesh.Updater.csproj",
    [string]$OutputDir = "$PSScriptRoot\build"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\ReleaseHelpers.ps1"

$ProjectPath = (Resolve-Path $ProjectPath).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

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

$publishDir = Join-Path $OutputDir "updater-publish"
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

if (-not (Test-Path (Join-Path $publishDir "OrbitMesh.Updater.dll"))) {
    throw "OrbitMesh.Updater.dll not found in publish output - refusing to package this."
}

Write-Step "Hashing published files"
$files = Get-FileHashesRecursive -Root $publishDir
Write-Host "$($files.Count) files hashed"

$manifestPath = Join-Path $publishDir "manifest.json"
New-ReleaseManifest -Version $Version -Roots @(".") -Files $files -OutFile $manifestPath | Out-Null
Write-Host "manifest.json written ($((Get-Item $manifestPath).Length) bytes)"

$sigPath = $null
if ($PrivateKeyPath) {
    Write-Step "Signing manifest.json (RS256)"
    $sigPath = New-ReleaseSignature -ManifestPath $manifestPath -PrivateKeyPath $PrivateKeyPath -Passphrase $Passphrase
    Write-Host "Signed: $sigPath"
} else {
    Write-Host "No -PrivateKeyPath given - release will be unsigned." -ForegroundColor Yellow
}

Write-Step "Creating release archive"
$zipName = "orbitmesh-updater-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName
New-ZipArchive -SourceDir $publishDir -DestinationZip $zipPath

Copy-Item $manifestPath (Join-Path $OutputDir "manifest.json") -Force
if ($sigPath) {
    Copy-Item $sigPath (Join-Path $OutputDir "manifest.json.sig") -Force
}

Write-Host ""
Write-Host "Updater built successfully." -ForegroundColor Green
Write-Host "Version : $Version"
Write-Host "Files   : $($files.Count)"
Write-Host "Archive : $zipPath"
Write-Host "Signed  : $(if ($sigPath) { 'yes' } else { 'no' })"
Write-Host ""
Write-Host "Upload $zipName to your update server as the 'orbitmesh-updater' project, and also deploy it"
Write-Host "once per host, extracted into a sibling 'updater' folder next to the Server/Edge install dir."
