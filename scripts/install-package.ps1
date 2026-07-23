[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Chrome', 'Edge', 'Both')]
    [string]$Browser = 'Both',

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\DevOpsReview\0.2.1'),

    [string]$ConfigurationPath = (Join-Path $env:LOCALAPPDATA 'DevOpsReview\config.json')
)

$ErrorActionPreference = 'Stop'
$hostName = 'com.lus.devops_review'
$extensionId = 'kldpfliioeaahafemncagclpehbnblig'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bridgeSource = Join-Path $packageRoot 'bridge'
$extensionSource = Join-Path $packageRoot 'extension'
$installRootFull = [IO.Path]::GetFullPath($InstallRoot)
$configurationFull = [IO.Path]::GetFullPath($ConfigurationPath)

if (-not (Test-Path -LiteralPath (Join-Path $bridgeSource 'DevOpsReview.Bridge.exe'))) {
    throw "The package does not contain bridge\DevOpsReview.Bridge.exe."
}
if (-not (Test-Path -LiteralPath (Join-Path $extensionSource 'manifest.json'))) {
    throw "The package does not contain extension\manifest.json."
}

if ($PSCmdlet.ShouldProcess($installRootFull, 'Install packaged Review Bridge')) {
    New-Item -ItemType Directory -Force -Path $installRootFull | Out-Null
    Copy-Item -Path (Join-Path $bridgeSource '*') -Destination $installRootFull -Recurse -Force
}

$manifestPath = Join-Path $installRootFull "$hostName.json"
$manifest = [ordered]@{
    name = $hostName
    description = 'Local Codex bridge for Azure DevOps pull request review'
    path = Join-Path $installRootFull 'DevOpsReview.Bridge.exe'
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}
if ($PSCmdlet.ShouldProcess($manifestPath, 'Write Native Messaging host manifest')) {
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
}

if (-not (Test-Path -LiteralPath $configurationFull) -and
    $PSCmdlet.ShouldProcess($configurationFull, 'Create configuration from example')) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $configurationFull) | Out-Null
    Copy-Item -LiteralPath (Join-Path $packageRoot 'config.example.json') -Destination $configurationFull
}

$registryPaths = switch ($Browser) {
    'Chrome' { "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName" }
    'Edge' { "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName" }
    'Both' {
        "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
        "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
    }
}
foreach ($registryPath in $registryPaths) {
    if ($PSCmdlet.ShouldProcess($registryPath, 'Register Native Messaging host')) {
        New-Item -Path $registryPath -Force | Out-Null
        Set-Item -Path $registryPath -Value $manifestPath
    }
}

Write-Host "Bridge installed: $(Join-Path $installRootFull 'DevOpsReview.Bridge.exe')"
Write-Host "Configuration: $configurationFull"
Write-Host "Load unpacked extension from: $extensionSource"
Write-Host "Extension ID: $extensionId"
Write-Host 'Complete the configuration before using the extension.'
