#Requires -Version 7.2
$ErrorActionPreference='Stop'
$helper=Join-Path $PSScriptRoot 'tool-paths.ps1'
if(!(Test-Path -LiteralPath $helper)){throw 'FAIL: portable tool resolution is missing.'}
. $helper
$oldGodot=$env:LIBREKO_GODOT;$oldMysql=$env:LIBREKO_MYSQL_BIN;$oldPath=$env:PATH
try {
    $exe=Join-Path $PSHOME 'pwsh.exe'
    if((Resolve-GodotEditor -Path $exe) -ne [IO.Path]::GetFullPath($exe)){throw 'Explicit executable path was not used.'}
    $env:LIBREKO_GODOT=$exe
    if((Resolve-GodotEditor) -ne [IO.Path]::GetFullPath($exe)){throw 'Godot environment override was not used.'}
    $failed=$false;try {Resolve-GodotEditor -Path (Join-Path $PSHOME 'missing-godot.exe') | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'An invalid explicit executable path was silently ignored.'}
    $env:LIBREKO_GODOT='';$env:PATH=''
    $failed=$false;try {Resolve-GodotEditor | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'Missing Godot should give an actionable error.'}
    $env:LIBREKO_MYSQL_BIN=Join-Path $PSHOME 'missing-mysql'
    $failed=$false;try {Resolve-MySqlBin | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'Invalid MySQL tools should fail before starting a service.'}
    Write-Output 'PASS: explicit tool path, environment override, invalid explicit path, missing Godot and incomplete MySQL directory'
} finally {$env:LIBREKO_GODOT=$oldGodot;$env:LIBREKO_MYSQL_BIN=$oldMysql;$env:PATH=$oldPath}
