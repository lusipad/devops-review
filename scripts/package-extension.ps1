[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\devops-review-extension.zip')
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$extensionRoot = Join-Path $repoRoot 'extension'
$outputFull = [IO.Path]::GetFullPath($OutputPath)
$staging = Join-Path $env:TEMP "devops-review-extension-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    $files = @(
        'manifest.json', 'shared.js', 'background.js', 'content.js',
        'panel.html', 'panel.css', 'panel.js',
        'options.html', 'options.js'
    )
    foreach ($file in $files) {
        Copy-Item -LiteralPath (Join-Path $extensionRoot $file) -Destination $staging
    }

    Get-Content -Raw -LiteralPath (Join-Path $staging 'manifest.json') | ConvertFrom-Json | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputFull) | Out-Null
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $outputFull -Force
    Write-Host "Extension package: $outputFull"
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse
    }
}
