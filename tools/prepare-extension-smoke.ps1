[CmdletBinding()]
param(
    [string]$ServerOrigin = 'http://localhost:8081/*'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repoRoot 'extension'
$target = Join-Path $repoRoot 'artifacts\extension-smoke'

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse

$manifestPath = Join-Path $target 'manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifest | Add-Member -NotePropertyName host_permissions -NotePropertyValue @($ServerOrigin)
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

Write-Output $target
