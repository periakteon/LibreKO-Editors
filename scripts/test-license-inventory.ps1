#Requires -Version 7.2
param([string]$Directory=(Join-Path (Split-Path $PSScriptRoot) 'licenses'))
$ErrorActionPreference='Stop'
$inventory=@(Get-Content -LiteralPath (Join-Path $Directory 'packages.json') -Raw | ConvertFrom-Json)
$ids=@($inventory | ForEach-Object {$_.package+'/'+$_.version})
if(@($ids | Sort-Object -Unique).Count -ne $ids.Count){throw 'Duplicate package identities in license inventory.'}
foreach($package in $inventory){
    if(!@($package.licenseFiles | Where-Object {$_ -match '(?i)(^|[-_/.])(LICEN[CS]E|COPYING)(\.|[-_]|$)' -and $_ -notmatch '(?i)THIRD[-_]?PARTY'}).Count){throw "Primary package license is not identified: $($package.package)/$($package.version)"}
    foreach($file in $package.licenseFiles){
        if([IO.Path]::GetFileName($file) -ne $file -or !(Test-Path -LiteralPath (Join-Path $Directory $file) -PathType Leaf)){throw 'License inventory references an invalid or missing file.'}
    }
}
Write-Output "PASS: $($inventory.Count) unique package records identify primary licenses and all referenced files exist"
