<#
.SYNOPSIS
    Installs OrbitMesh.Server or OrbitMesh.Edge as a Windows Service, with the restart settings
    OrbitMesh.Updater needs to manage updates without the Service Control Manager relaunching the
    old version mid-update.

.DESCRIPTION
    The SCM restarts a service on ANY unexpected stop by default - even a clean exit(0) - unless the
    "failure flag" (SERVICE_CONFIG_FAILURE_ACTIONS_FLAG) is set. That's exactly what happens during an
    update: the process exits cleanly (see OrbitMesh.Updating.ExitCodes.UpdatePending) right after
    handing off to OrbitMesh.Updater - without this flag the SCM would relaunch it immediately, into a
    file-locking race with Updater.

.EXAMPLE
    .\install-windows-service.ps1 -ServiceName OrbitMeshServer -BinaryPath "C:\OrbitMesh\server\OrbitMesh.Server.dll" -DisplayName "OrbitMesh Server"

.EXAMPLE
    .\install-windows-service.ps1 -ServiceName OrbitMeshEdge -BinaryPath "C:\OrbitMesh\edge\OrbitMesh.Edge.dll" -DisplayName "OrbitMesh Edge"
#>
param(
    [Parameter(Mandatory)][string]$ServiceName,
    [Parameter(Mandatory)][string]$BinaryPath,
    [string]$DisplayName = $ServiceName,
    [string]$DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BinaryPath)) {
    throw "BinaryPath '$BinaryPath' does not exist."
}
$BinaryPath = (Resolve-Path $BinaryPath).Path
$binPathName = "`"$DotnetPath`" `"$BinaryPath`""

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service '$ServiceName' already exists - updating its configuration."
    sc.exe config $ServiceName binPath= $binPathName start= auto | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName $binPathName -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

# Restart on a real crash, up to 3 times - the Windows equivalent of systemd's Restart=on-failure.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000
if ($LASTEXITCODE -ne 0) { throw "sc.exe failure failed (code $LASTEXITCODE)" }

# The whole point of this script - see the SYNOPSIS.
sc.exe failureflag $ServiceName 1
if ($LASTEXITCODE -ne 0) { throw "sc.exe failureflag failed (code $LASTEXITCODE)" }

Write-Host ""
Write-Host "Service '$ServiceName' installed/updated." -ForegroundColor Green
Write-Host "Start with: Start-Service $ServiceName"
