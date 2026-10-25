# Copies each package in vendor.json from its repo at the pinned ref into this project, like an npm install.
#   ./vendor.ps1                      all packages at their pinned ref
#   ./vendor.ps1 ocean                one package
#   ./vendor.ps1 ocean -Ref main      move the pin (vendor.json is updated to the resolved commit)
#   ./vendor.ps1 ocean -From D:/path  use a local checkout instead of the repo URL (its committed state only)
# The destination folder is replaced; a VENDOR.txt inside it records where it came from.
param([string]$Name, [string]$Ref, [string]$From)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifestPath = Join-Path $root 'vendor.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$cache = Join-Path $root '.scratch/vendor'

$names = if ($Name) { @($Name) } else { $manifest.PSObject.Properties.Name }
foreach ($n in $names) {
    $p = $manifest.$n
    if (-not $p) { throw "No package '$n' in vendor.json" }
    $want = if ($Ref) { $Ref } else { $p.ref }

    if ($From) { $repo = $From }
    else {
        $repo = Join-Path $cache $n
        if (-not (Test-Path (Join-Path $repo '.git'))) { git clone --quiet --no-checkout $p.repo $repo }
        git -C $repo fetch --quiet origin
        if ($LASTEXITCODE) { throw "fetch failed for $n" }
    }
    $commit = git -C $repo rev-parse "$want^{commit}" 2>$null
    if (-not $commit) { $commit = git -C $repo rev-parse "origin/$want^{commit}" }
    if (-not $commit) { throw "Ref '$want' not found in $repo" }

    $zip = Join-Path ([IO.Path]::GetTempPath()) "vendor_$n.zip"
    git -C $repo archive --format=zip -o $zip $commit $p.path
    if ($LASTEXITCODE) { throw "archive failed for $n" }
    $unpacked = Join-Path ([IO.Path]::GetTempPath()) "vendor_$n"
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    Expand-Archive $zip $unpacked
    Remove-Item $zip

    $dest = Join-Path $root $p.dest
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Move-Item (Join-Path $unpacked $p.path) $dest
    Remove-Item $unpacked -Recurse -Force
    "$($p.repo)`n$commit`n$($p.path)`nVendored by vendor.ps1; edit the source repo, not this copy.`n" |
        Set-Content (Join-Path $dest 'VENDOR.txt') -Encoding ascii -NoNewline

    if ($p.ref -ne $commit) {
        $p.ref = $commit
        $manifest | ConvertTo-Json | Set-Content $manifestPath -Encoding ascii
    }
    Write-Host "$n -> $($p.dest) @ $($commit.Substring(0, 8))"
}
