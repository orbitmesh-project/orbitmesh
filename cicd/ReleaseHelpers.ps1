<#
.SYNOPSIS
    Shared manifest/signing/hashing functions, dot-sourced by release-server.ps1 and
    release-static-site.ps1 so the two don't duplicate this logic.
#>

function Get-FileHashesRecursive {
    param([Parameter(Mandatory)][string]$Root, [string[]]$ExcludeRelativePaths = @())

    $exclude = $ExcludeRelativePaths | ForEach-Object { $_ -replace '\\', '/' }
    $files = [ordered]@{}
    Get-ChildItem $Root -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($Root.Length + 1) -replace '\\', '/'
        if ($exclude -contains $relative) {
            return
        }
        $files[$relative] = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $files
}

function New-ReleaseManifest {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string[]]$Roots,
        [Parameter(Mandatory)]$Files,
        [Parameter(Mandatory)][string]$OutFile
    )

    $manifest = [ordered]@{
        version      = $Version
        date         = (Get-Date).ToString("yyyy-MM-dd")
        generated_at = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        roots        = $Roots
        files        = $Files
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    # Explicit no-BOM UTF8: Set-Content/Out-File default to BOM-prefixed UTF8 in Windows
    # PowerShell 5.1, and the signature covers these exact bytes.
    [System.IO.File]::WriteAllText($OutFile, $json, (New-Object System.Text.UTF8Encoding($false)))
    return $OutFile
}

function New-ReleaseSignature {
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$PrivateKeyPath,
        [string]$Passphrase
    )

    $opensslCmd = Get-Command openssl -ErrorAction SilentlyContinue
    if (-not $opensslCmd) {
        throw "openssl.exe not found on PATH. It ships with Git for Windows (Git\mingw64\bin) - install Git or add it to PATH."
    }

    $sigBinPath = "$ManifestPath.bin"
    $opensslArgs = @("dgst", "-sha256", "-sign", $PrivateKeyPath, "-out", $sigBinPath)
    if ($Passphrase) {
        $opensslArgs += @("-passin", "pass:$Passphrase")
    }
    $opensslArgs += $ManifestPath

    & $opensslCmd.Source @opensslArgs
    if ($LASTEXITCODE -ne 0) {
        throw "openssl signing failed with exit code $LASTEXITCODE"
    }

    $signatureB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sigBinPath))
    Remove-Item $sigBinPath -Force

    # Same envelope shape as Forgelabme\Ci4Updater\Libraries\ReleaseSignature::sign():
    # {"alg":"RS256","signature":"<base64>"}
    $envelope = [ordered]@{ alg = "RS256"; signature = $signatureB64 } | ConvertTo-Json -Compress
    $sigPath = "$ManifestPath.sig"
    [System.IO.File]::WriteAllText($sigPath, "$envelope`n", (New-Object System.Text.UTF8Encoding($false)))
    return $sigPath
}

function New-ZipArchive {
    param([Parameter(Mandatory)][string]$SourceDir, [Parameter(Mandatory)][string]$DestinationZip)

    # Neither Compress-Archive nor [System.IO.Compression.ZipFile]::CreateFromDirectory() can be used
    # here: both write entry names using Windows' backslash as the path separator under Windows
    # PowerShell 5.1's .NET Framework (a long-fixed-on-.NET-Core bug that never got backported). The
    # ZIP format itself requires forward slashes - a backslash is just an ordinary filename character
    # on Linux, so OrbitMesh.Server's ZipFile.ExtractToDirectory (real .NET, no such bug) extracts
    # a "css\bootstrap.css" entry as one flat file literally named that, not nested under css/ at all.
    # Building entries by hand with an explicitly forward-slashed name sidesteps the bug entirely.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $DestinationZip) {
        Remove-Item $DestinationZip -Force
    }
    $zip = [System.IO.Compression.ZipFile]::Open($DestinationZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $SourceDir -Recurse -File | ForEach-Object {
            $entryName = $_.FullName.Substring($SourceDir.Length + 1) -replace '\\', '/'
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
}

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}
