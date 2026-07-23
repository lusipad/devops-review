[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Chrome', 'Edge', 'Both')]
    [string]$Browser = 'Both',

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\DevOpsReview\0.2.0'),

    [string]$ConfigurationPath = (Join-Path $env:LOCALAPPDATA 'DevOpsReview\config.json'),

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$hostName = 'com.lus.devops_review'
$extensionId = 'kldpfliioeaahafemncagclpehbnblig'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installRootFull = [IO.Path]::GetFullPath($InstallRoot)
$configurationFull = [IO.Path]::GetFullPath($ConfigurationPath)
$publishDirectory = Join-Path $repoRoot 'artifacts\bridge-win-x64'

if (-not $SkipBuild) {
    if ($PSCmdlet.ShouldProcess($publishDirectory, 'Publish self-contained Review Bridge')) {
        dotnet publish (Join-Path $repoRoot 'src\DevOpsReview.Bridge\DevOpsReview.Bridge.csproj') `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --output $publishDirectory `
            -p:PublishSingleFile=true
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
    }
}

$bridgeExecutable = Join-Path $publishDirectory 'DevOpsReview.Bridge.exe'
if (-not (Test-Path -LiteralPath $bridgeExecutable)) {
    throw "Published Bridge executable was not found: $bridgeExecutable"
}

if ($PSCmdlet.ShouldProcess($installRootFull, 'Install Review Bridge files')) {
    New-Item -ItemType Directory -Force -Path $installRootFull | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $installRootFull -Recurse -Force
}

$installedExecutable = Join-Path $installRootFull 'DevOpsReview.Bridge.exe'
$manifestPath = Join-Path $installRootFull "$hostName.json"
$manifest = [ordered]@{
    name = $hostName
    description = 'Local Codex bridge for Azure DevOps pull request review'
    path = $installedExecutable
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
}

if ($PSCmdlet.ShouldProcess($manifestPath, 'Write Native Messaging host manifest')) {
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
}

$configurationDirectory = Split-Path -Parent $configurationFull
if (-not (Test-Path -LiteralPath $configurationFull) -and
    $PSCmdlet.ShouldProcess($configurationFull, 'Create configuration from example')) {
    New-Item -ItemType Directory -Force -Path $configurationDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'config.example.json') -Destination $configurationFull
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

Write-Host "Bridge installed: $installedExecutable"
Write-Host "Configuration: $configurationFull"
Write-Host "Extension directory: $(Join-Path $repoRoot 'extension')"
Write-Host "Extension ID: $extensionId"
Write-Host 'Edit the configuration before opening the extension side panel.'
