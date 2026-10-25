param([string]$GodotEditor='',[string]$FixtureOwnerRoot='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'prepare-source-checks.ps1') -FixtureOwnerRoot $FixtureOwnerRoot
. (Join-Path $PSScriptRoot 'tool-paths.ps1')
if(!$GodotEditor){$GodotEditor=Resolve-GodotEditor}
$GodotEditor=[IO.Path]::GetFullPath($GodotEditor)
if(!(Test-Path -LiteralPath $GodotEditor -PathType Leaf)){throw 'Select the Godot4.7.2 Mono editor with -GodotEditor.'}
$env:LIBREKO_AUTHORING_GODOT=$GodotEditor
Write-Output "Configured native build/probe editor: $GodotEditor"
