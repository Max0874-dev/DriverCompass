# Opens every fixed driver page link in data/vendors.json and reports dead ones.
# Run manually or from the scheduled GitHub Action (.github/workflows/check-vendor-links.yml).
# Only public vendor pages are requested; no device data is involved.
#
# Fails (exit code 1) only on clear errors:
#   - HTTP 404 or 410
#   - host name does not resolve, or the HTTPS certificate is invalid
#   - the final address after redirects is outside the vendor's allowedDomains
# Everything else that is not a success (403, 429, 5xx, timeouts) is only a warning,
# because many vendor sites block automated requests although the page works in a browser.
# Templates with placeholders such as {serial} are skipped.
[CmdletBinding()]
param(
    [string]$Path,
    [int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not set yet in param defaults under Windows PowerShell 5.1.
if (-not $Path) { $Path = Join-Path (Join-Path $PSScriptRoot '..') (Join-Path 'data' 'vendors.json') }

$isWin = ($env:OS -eq 'Windows_NT')
$curl = if ($isWin) { 'curl.exe' } else { 'curl' }
$nullFile = if ($isWin) { 'NUL' } else { '/dev/null' }
$userAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36 DriverCompass-LinkCheck'

$catalog = Get-Content -Raw -Encoding UTF8 $Path | ConvertFrom-Json

$links = New-Object System.Collections.Generic.List[object]
$skipped = 0
foreach ($v in $catalog.chipVendors) {
    foreach ($p in $v.driverPages) {
        $links.Add([pscustomobject]@{ Vendor = "chip/$($v.id)"; Url = $p.url; Domains = @($v.allowedDomains) })
    }
}
foreach ($o in $catalog.oems) {
    foreach ($s in $o.supportUrls) {
        if ($s.template -match '\{') { $skipped++; continue }
        $links.Add([pscustomobject]@{ Vendor = "oem/$($o.id)"; Url = $s.template; Domains = @($o.allowedDomains) })
    }
}

function Test-AllowedHost([string]$hostName, [string[]]$domains) {
    foreach ($d in $domains) {
        if ($hostName -eq $d -or $hostName.EndsWith(".$d")) { return $true }
    }
    return $false
}

$results = foreach ($link in $links) {
    $out = & $curl --silent --location --max-redirs 10 --max-time $TimeoutSeconds `
        --user-agent $userAgent --header 'Accept: text/html' --header 'Accept-Language: en-US,en;q=0.8' `
        --output $nullFile --write-out '%{http_code} %{url_effective}' $link.Url 2>$null
    $curlExit = $LASTEXITCODE
    $code, $final = "$out" -split ' ', 2
    $level = 'ok'
    $detail = ''

    if ($curlExit -eq 6) { $level = 'error'; $detail = 'host name does not resolve' }
    elseif ($curlExit -eq 60) { $level = 'error'; $detail = 'invalid HTTPS certificate' }
    elseif ($curlExit -ne 0) { $level = 'warning'; $detail = "no answer (curl exit code $curlExit)" }
    else {
        $finalHost = ([Uri]$final).Host.ToLowerInvariant()
        if ($code -in @('404', '410')) { $level = 'error'; $detail = "HTTP $code" }
        elseif (-not (Test-AllowedHost $finalHost $link.Domains)) { $level = 'error'; $detail = "ends up on $finalHost, outside allowedDomains" }
        elseif ([int]$code -ge 400) { $level = 'warning'; $detail = "HTTP $code (often bot protection, check in a browser)" }
        elseif ($final -ne $link.Url) { $detail = "redirects to $final" }
    }

    [pscustomobject]@{ Level = $level; Vendor = $link.Vendor; Url = $link.Url; Detail = $detail }
}

$errors = @($results | Where-Object Level -eq 'error')
$warnings = @($results | Where-Object Level -eq 'warning')

foreach ($r in $results) {
    $line = "{0,-8} {1,-26} {2} {3}" -f $r.Level.ToUpper(), $r.Vendor, $r.Url, $r.Detail
    Write-Host $line.TrimEnd()
    if ($env:GITHUB_ACTIONS -eq 'true' -and $r.Level -ne 'ok') {
        Write-Host "::$($r.Level)::$($r.Vendor): $($r.Url) - $($r.Detail)"
    }
}

$summary = "$($results.Count) links checked, $($errors.Count) errors, $($warnings.Count) warnings, $skipped templates with placeholders skipped."
Write-Host ''
Write-Host $summary

if ($env:GITHUB_STEP_SUMMARY) {
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('## Vendor link check')
    $md.Add('')
    $md.Add($summary)
    if ($errors.Count + $warnings.Count -gt 0) {
        $md.Add('')
        $md.Add('| Level | Vendor | URL | Detail |')
        $md.Add('|---|---|---|---|')
        foreach ($r in @($errors) + @($warnings)) { $md.Add("| $($r.Level) | $($r.Vendor) | $($r.Url) | $($r.Detail) |") }
    }
    $md | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($errors.Count -gt 0) { exit 1 }
