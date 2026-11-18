# Downloads the latest pci.ids (https://pci-ids.ucw.cz/, GPL-2.0-or-later OR BSD-3-Clause; we use BSD-3-Clause)
# and replaces data/third-party/pci.ids. Run manually or from the scheduled GitHub Action, then commit.
$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '..\data\third-party\pci.ids'
$tmp = "$target.download"

Invoke-WebRequest -Uri 'https://pci-ids.ucw.cz/v2.2/pci.ids' -OutFile $tmp -UseBasicParsing

$head = Get-Content $tmp -TotalCount 20 -Encoding UTF8
if (-not ($head -match '3-clause BSD License')) {
    Remove-Item $tmp
    throw 'License header changed – review before updating (expected GPL-2.0+ OR BSD-3-Clause).'
}
if ((Get-Item $tmp).Length -lt 500KB) {
    Remove-Item $tmp
    throw 'Downloaded file is unexpectedly small.'
}

Move-Item $tmp $target -Force
Write-Host "pci.ids updated: $((Get-Content $target -TotalCount 5 | Select-String 'Version').Line)"
