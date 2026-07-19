[CmdletBinding()]
param(
    [string]$Version = '0.1.0',

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\devops-review-$Version-win-x64.zip"
}
$outputFull = [IO.Path]::GetFullPath($OutputPath)
$publishDirectory = Join-Path $repoRoot 'artifacts\bridge-win-x64'
$staging = Join-Path $env:TEMP "devops-review-release-$([Guid]::NewGuid().ToString('N'))"
$packageRoot = Join-Path $staging "devops-review-$Version-win-x64"

try {
    dotnet publish (Join-Path $repoRoot 'src\DevOpsReview.Bridge\DevOpsReview.Bridge.csproj') `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $packageRoot | Out-Null
    Copy-Item -LiteralPath $publishDirectory -Destination (Join-Path $packageRoot 'bridge') -Recurse
    Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bridge') -Filter '*.pdb' | Remove-Item
    Copy-Item -LiteralPath (Join-Path $repoRoot 'extension') -Destination (Join-Path $packageRoot 'extension') -Recurse
    Remove-Item -LiteralPath (Join-Path $packageRoot 'extension\tests') -Recurse
    Remove-Item -LiteralPath (Join-Path $packageRoot 'extension\package.json')

    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'scripts') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\install-package.ps1') -Destination (Join-Path $packageRoot 'scripts')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\uninstall.ps1') -Destination (Join-Path $packageRoot 'scripts')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'config.example.json') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Destination $packageRoot

    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination (Join-Path $packageRoot 'docs') -Recurse

    $hashFiles = @(
        'bridge\DevOpsReview.Bridge.exe',
        'extension\manifest.json',
        'scripts\install-package.ps1',
        'config.example.json'
    )
    $checksums = foreach ($relativePath in $hashFiles) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $packageRoot $relativePath)
        "$($hash.Hash.ToLowerInvariant())  $($relativePath.Replace('\', '/'))"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding ascii

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputFull) | Out-Null
    Compress-Archive -Path $packageRoot -DestinationPath $outputFull -Force
    $archiveHash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputFull
    "$($archiveHash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($outputFull))" |
        Set-Content -LiteralPath "$outputFull.sha256" -Encoding ascii
    Write-Host "Release package: $outputFull"
    Write-Host "Release checksum: $outputFull.sha256"
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse
    }
}
