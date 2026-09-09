[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '../.dependencies/espeak')
)

$ErrorActionPreference = 'Stop'
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64' -or $env:OS -ne 'Windows_NT') {
    throw 'This helper supports Windows x64. Install eSpeak NG using your platform package manager on other systems.'
}

# Download the native distribution used by upstream Python. No Python is installed or invoked.
$url = 'https://files.pythonhosted.org/packages/9d/ed/a3d872fbad4f3a3f3db0e8c31768ab14e77cd77306de16b8b20b1e1df7ea/espeakng_loader-0.2.4-py3-none-win_amd64.whl'
$sha256 = '41f1e08ac9deda2efd1ea9de0b81dab9f5ae3c4b24284f76533d0a7b1dd7abd7'
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$archivePath = Join-Path ([System.IO.Path]::GetTempPath()) ('kittentts-espeak-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    Write-Host 'Downloading eSpeak NG native library and dictionaries (espeakng-loader 0.2.4)...'
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archivePath
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $sha256) {
        throw 'eSpeak NG download checksum did not match.'
    }
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $destinationPath -Force
    $libraryPath = Join-Path $destinationPath 'espeakng_loader/espeak-ng.dll'
    $dataPath = Join-Path $destinationPath 'espeakng_loader/espeak-ng-data'
    if (!(Test-Path -LiteralPath $libraryPath) -or !(Test-Path -LiteralPath $dataPath)) {
        throw 'eSpeak NG archive did not contain the expected native library and dictionaries.'
    }
    Write-Host "Installed: $libraryPath"
    Write-Host 'The CLI discovers this installation when run from the repository root.'
    Write-Host "For another working directory, set ESPEAK_LIBRARY_PATH=$libraryPath"
    Write-Host "and ESPEAK_DATA_PATH=$dataPath"
} finally {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
}
