[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\DevOpsReview\0.2.1'),

    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$hostName = 'com.lus.devops_review'
$registryPaths = @(
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName",
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
)

foreach ($registryPath in $registryPaths) {
    if ((Test-Path -LiteralPath $registryPath) -and
        $PSCmdlet.ShouldProcess($registryPath, 'Remove Native Messaging registration')) {
        Remove-Item -LiteralPath $registryPath -Recurse
    }
}

$localPrograms = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\DevOpsReview'))
$installRootFull = [IO.Path]::GetFullPath($InstallRoot)
if (-not $installRootFull.StartsWith(
    $localPrograms + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallRoot must remain under $localPrograms"
}

if ((Test-Path -LiteralPath $installRootFull) -and
    $PSCmdlet.ShouldProcess($installRootFull, 'Remove installed Bridge version')) {
    Remove-Item -LiteralPath $installRootFull -Recurse
}

if ($RemoveData) {
    $dataRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DevOpsReview'))
    if ((Test-Path -LiteralPath $dataRoot) -and
        $PSCmdlet.ShouldProcess($dataRoot, 'Remove configuration, sessions, and worktrees')) {
        Remove-Item -LiteralPath $dataRoot -Recurse
    }
}
