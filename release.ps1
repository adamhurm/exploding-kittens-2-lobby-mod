#Requires -Version 7
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$files = @(
    "src\EKLobbyMod\Plugin.cs",
    "docs\index.html",
    "hosting\ek.bring-us.com\public\index.html",
    "hosting\ek.bring-us.com\public\get"
)

# Detect current version from Plugin.cs
$pluginCs = Get-Content "src\EKLobbyMod\Plugin.cs" -Raw
if ($pluginCs -match 'PluginVersion = "(\d+\.\d+\.\d+)"') {
    $oldVersion = $Matches[1]
} else {
    Write-Error "Could not detect current version in Plugin.cs"
}

Write-Host "Bumping v$oldVersion -> v$Version"

foreach ($file in $files) {
    $content = Get-Content $file -Raw
    $updated = $content -replace [regex]::Escape($oldVersion), $Version
    $updated = $updated -replace [regex]::Escape("v$oldVersion"), "v$Version"
    Set-Content $file $updated -NoNewline
    Write-Host "  updated $file"
}

git add ($files | ForEach-Object { $_ })
git commit -m "chore: bump version to v$Version"
git push origin main

gh release create "v$Version" --title "v$Version" --notes "Release v$Version." --repo adamhurm/exploding-kittens-2-lobby-mod

Write-Host "`nv$Version released. CI: https://github.com/adamhurm/exploding-kittens-2-lobby-mod/actions"
