<#
.SYNOPSIS
    Fresh install of OrbitMesh.Server (+ OrbitMesh.Edge optionally) and the Console on Windows,
    Windows Services included.

.DESCRIPTION
    For each component: GET /api/{slug}/latest.json (same wire format as ReleaseServerClient.cs),
    downloads zip_url, extracts into -InstallDir\{server,edge,console}. Never overwrites an existing
    directory without confirmation - a fresh-install tool, not an update one (see
    ServerSelfUpdater/StaticSiteUpdater for the built-in auto-update, already wired via updateOptions
    below).

    Regenerates appsettings.json for the server (no preconfigured credentials - the Console offers to
    create the administrator on first access via GetSetupStatus/CreateAdmin) and for the edge (no
    AccessKey yet - approve it from the Console's pending edges list once running), then installs
    Windows Services via install-windows-service.ps1.

    Installs under Program Files by default, so needs an elevated PowerShell prompt throughout, not
    just for the Services step.

.EXAMPLE
    .\install.ps1

.EXAMPLE
    .\install.ps1 -InstallDir C:\OrbitMesh -Port 8088 -InstallEdge:$false
#>
param(
    [string]$InstallDir,
    [string]$UpdateServer = "https://updates.orbitmesh.org",
    [int]$Port,
    [Nullable[bool]]$InstallEdge,
    [Nullable[bool]]$SetupServices
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This installs under Program Files and sets up Windows Services - run it from an elevated PowerShell prompt."
}

function Ask([string]$Prompt, [string]$Default) {
    if ([Console]::IsInputRedirected) {
        return $Default
    }
    $answer = Read-Host "$Prompt [$Default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer
}

function IsYes([string]$Answer) {
    return $Answer -match '^(y|yes|o|oui)$'
}

Write-Host "Fresh OrbitMesh install"
Write-Host "========================"

if (-not $InstallDir) { $InstallDir = Ask "Install directory" (Join-Path $env:ProgramFiles "OrbitMesh") }
if (-not $Port) { $Port = [int](Ask "Server listen port" "8088") }
if ($null -eq $InstallEdge) { $InstallEdge = IsYes (Ask "Also install OrbitMesh.Edge on this machine? (y/n)" "y") }
if ($null -eq $SetupServices) { $SetupServices = IsYes (Ask "Set up Windows Services (auto-start)? (y/n)" "y") }

$DotNetChannel = "10.0"

function Install-DotNet {
    $installer = Join-Path ([System.IO.Path]::GetTempPath()) "dotnet-install-$([Guid]::NewGuid().ToString('N')).ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    # Out-Null in case the installer writes to the success stream, not just Write-Host - that would
    # otherwise mix into this function's own return value via `$DotnetPath = Install-DotNet`.
    & $installer -Channel $DotNetChannel -Runtime aspnetcore | Out-Null
    Remove-Item $installer -Force

    $dotnetPath = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (-not (Test-Path $dotnetPath)) {
        throw ".NET install seems to have failed - $dotnetPath not found afterward."
    }
    return $dotnetPath
}

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    $DotnetPath = $dotnetCmd.Source
} else {
    $localAppDataDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-Path $localAppDataDotnet) {
        $DotnetPath = $localAppDataDotnet
    } else {
        Write-Host "dotnet not found (not on PATH, not in the usual location)."
        $installDotNetDefault = if ([Console]::IsInputRedirected) { "n" } else { "y" }
        $installDotNetAnswer = Ask "Install .NET $DotNetChannel now via Microsoft's official script? (y/n)" $installDotNetDefault
        if (IsYes $installDotNetAnswer) {
            $DotnetPath = Install-DotNet
        } else {
            throw "dotnet is required - install it and re-run this script."
        }
    }
}
Write-Host "dotnet found: $DotnetPath"

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# Returns $true if installed, $false if skipped (already present, unavailable, or declined).
function Install-Component([string]$Slug, [string]$Subdir) {
    $target = Join-Path $InstallDir $Subdir

    Write-Host ""
    Write-Host "==> $Slug" -ForegroundColor Cyan

    if (Test-Path $target) {
        if (-not [Console]::IsInputRedirected) {
            $answer = Read-Host "    $target already exists - overwrite it? (y/N)"
            if (-not (IsYes $answer)) {
                Write-Host "    skipped (existing directory kept)." -ForegroundColor Yellow
                return $false
            }
            Remove-Item $target -Recurse -Force
        } else {
            Write-Host "    $target already exists - skipped (non-interactive, never overwrites without confirmation)." -ForegroundColor Yellow
            return $false
        }
    }

    try {
        $latest = Invoke-RestMethod -Uri "$UpdateServer/api/$Slug/latest.json" -ErrorAction Stop
    } catch {
        Write-Host "    no response from the update server - skipped." -ForegroundColor Yellow
        return $false
    }

    if (-not $latest.version -or $latest.version -eq "0" -or -not $latest.zip_url) {
        Write-Host "    no version published yet - skipped." -ForegroundColor Yellow
        return $false
    }
    Write-Host "    version $($latest.version)"

    $tmpZip = Join-Path ([System.IO.Path]::GetTempPath()) "orbitmesh-install-$([Guid]::NewGuid().ToString('N')).zip"
    try {
        Invoke-WebRequest -Uri $latest.zip_url -OutFile $tmpZip -ErrorAction Stop
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Expand-Archive -Path $tmpZip -DestinationPath $target -Force -ErrorAction Stop
    } catch {
        Write-Host "    download/extraction failed ($($latest.zip_url)) - skipped. Check that this file actually exists on the update server." -ForegroundColor Yellow
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Yellow
        Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    } finally {
        Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    }

    Write-Host "    installed in $target" -ForegroundColor Green
    return $true
}

$serverInstalled = Install-Component "orbitmesh-server" "server"
$edgeInstalled = $false
if ($InstallEdge) {
    $edgeInstalled = Install-Component "orbitmesh-edge" "edge"
}
Install-Component "orbitmesh-console" "console" | Out-Null
# Shared by Server's and Edge's self-update (ResolveUpdaterPath looks for a sibling "updater" dir).
$updaterInstalled = Install-Component "orbitmesh-updater" "updater"

# A release zip shouldn't contain appsettings.json, but never trust it either - always regenerate
# for a fresh install (see ServerSelfUpdater/StaticSiteUpdater).
if ($serverInstalled) {
    Write-Host ""
    Write-Host "==> server configuration" -ForegroundColor Cyan
    $serverConfig = @"
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*",
  "OrbitMesh": {
    "packagesRootDirectory": "packages",
    "listenUrls": [ "http://+:$Port" ],
    "fileServers": [
      {
        "enable": true,
        "localhostOnly": false,
        "path": "/console-next",
        "physicalPath": "../console",
        "updateProjectSlug": "orbitmesh-console",
        "preserveFiles": [],
        "isSpa": true
      }
    ],
    "allowedOrigins": [],
    "recoveryOptions": {
      "restartAfterFailure": true,
      "numberOfRetry": 3,
      "resetCounterAfterMinutes": 15,
      "restartPackageAfterSeconds": 30
    },
    "credentials": [],
    "edges": [],
    "variables": [],
    "updateOptions": {
      "serverUrl": "$UpdateServer",
      "projectSlug": "orbitmesh-server",
      "token": null,
      "checkIntervalMinutes": 5,
      "publicKeys": [ "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqNU78UjlPKzWjCHkllAI\nBimgx61ipo7mfb3ytLXd0sZZ9qTNs0IZBDsNY+Nm/tXAQvO0otEslMavj+sCUN6E\nt58wMRVVvqb29QVBQ5JlKkBQzeJTTM3tFADdbbJBq0Aer+wDo79ahTvojkaE8oTn\nlqT8A+YrUoO0cCu6AH9fOflg51v5jhkJTBNre2W1T2nFXgJ0WWRgMgjY6N7MZ56F\n+zwGn7fRPpMx/G7izvVtJ8HMBZjHNxFdXIB1ieVlRAxnm7k5hmziQ1hzTw0N1re0\naoAdc/x4FsYwqG2eaJuBtboAOVG3X8Q8zKimx3bdZEpMIxAAZ/KJpX0GXEygQbqJ\n6QIDAQAB\n-----END PUBLIC KEY-----" ],
      "serviceOrUnitName": "OrbitMeshServer",
      "updaterExecutablePath": null
    },
    "nuGetFeeds": [
      { "name": "OrbitMesh", "serviceIndexUrl": "https://nuget.orbitmesh.org/feeds/OrbitMesh/v3/index.json", "enable": true }
    ]
  }
}
"@
    [System.IO.File]::WriteAllText((Join-Path $InstallDir "server\appsettings.json"), $serverConfig, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "    appsettings.json written - no credentials preconfigured, the Console will offer to create the administrator on first access." -ForegroundColor Green
}

if ($edgeInstalled) {
    Write-Host ""
    Write-Host "==> edge configuration" -ForegroundColor Cyan
    $edgeConfig = @"
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.Hosting.Lifetime": "Information" }
  },
  "Edge": {
    "OrbitMeshServerUri": "http://localhost:$Port",
    "OrbitMeshAccessKey": "",
    "LocalPackagesDirectory": "Packages",
    "EdgeName": null,
    "ReportPackageUsageIntervalMs": 1000,
    "ShutdownPackageTimeoutMs": 10000,
    "UpdateOptions": {
      "serverUrl": "$UpdateServer",
      "projectSlug": "orbitmesh-edge",
      "token": null,
      "checkIntervalMinutes": 60,
      "publicKeys": [ "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqNU78UjlPKzWjCHkllAI\nBimgx61ipo7mfb3ytLXd0sZZ9qTNs0IZBDsNY+Nm/tXAQvO0otEslMavj+sCUN6E\nt58wMRVVvqb29QVBQ5JlKkBQzeJTTM3tFADdbbJBq0Aer+wDo79ahTvojkaE8oTn\nlqT8A+YrUoO0cCu6AH9fOflg51v5jhkJTBNre2W1T2nFXgJ0WWRgMgjY6N7MZ56F\n+zwGn7fRPpMx/G7izvVtJ8HMBZjHNxFdXIB1ieVlRAxnm7k5hmziQ1hzTw0N1re0\naoAdc/x4FsYwqG2eaJuBtboAOVG3X8Q8zKimx3bdZEpMIxAAZ/KJpX0GXEygQbqJ\n6QIDAQAB\n-----END PUBLIC KEY-----" ],
      "serviceOrUnitName": "OrbitMeshEdge",
      "updaterExecutablePath": null
    }
  }
}
"@
    [System.IO.File]::WriteAllText((Join-Path $InstallDir "edge\appsettings.json"), $edgeConfig, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "    appsettings.json written - no AccessKey yet, approve this Edge from the Console once the server is up (see below)." -ForegroundColor Green
}

if ($SetupServices) {
    if ($serverInstalled) {
        Write-Host ""
        Write-Host "==> OrbitMeshServer Windows Service" -ForegroundColor Cyan
        & "$PSScriptRoot\install-windows-service.ps1" -ServiceName "OrbitMeshServer" -BinaryPath (Join-Path $InstallDir "server\OrbitMesh.Server.dll") -DisplayName "OrbitMesh Server" -DotnetPath $DotnetPath
        Start-Service -Name "OrbitMeshServer"
        Write-Host "    started." -ForegroundColor Green
    }
    if ($edgeInstalled) {
        Write-Host ""
        Write-Host "==> OrbitMeshEdge Windows Service" -ForegroundColor Cyan
        & "$PSScriptRoot\install-windows-service.ps1" -ServiceName "OrbitMeshEdge" -BinaryPath (Join-Path $InstallDir "edge\OrbitMesh.Edge.dll") -DisplayName "OrbitMesh Edge" -DotnetPath $DotnetPath
        Start-Service -Name "OrbitMeshEdge"
        Write-Host "    started (will show as pending in the Console until approved - see below)." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Done. Install directory: $InstallDir" -ForegroundColor Green
if ($serverInstalled) {
    Write-Host ""
    Write-Host "1. Open http://<this-machine>:$Port/console-next/ (trailing slash included) and create the"
    Write-Host "   administrator account - no credentials are preconfigured."
}
if ($edgeInstalled) {
    Write-Host "2. Go to Edges > Pending edges and approve this Edge - the AccessKey is applied"
    Write-Host "   automatically, no manual configuration needed."
}
if (-not $updaterInstalled) {
    Write-Host ""
    Write-Host "Note: orbitmesh-updater isn't published on this update server yet, so self-update won't"
    Write-Host "work until it is - see cicd/release-updater.ps1."
}
