param([Parameter(Mandatory=$true)][string]$Manifest,[string]$GodotEditor='',[string]$EditorExe='')
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
$Manifest=[IO.Path]::GetFullPath($Manifest)
if(!(Test-Path -LiteralPath $Manifest -PathType Leaf)){throw 'Build manifest does not exist.'}
. (Join-Path $PSScriptRoot 'tool-paths.ps1')
if(!$GodotEditor){$GodotEditor=Resolve-GodotEditor}
if(!(Test-Path -LiteralPath $GodotEditor -PathType Leaf)){throw 'Pass the trusted Godot Mono editor with -GodotEditor.'}
$previousManifest=$env:LIBREKO_CLIENT_BUILD_MANIFEST
$previousGodot=$env:LIBREKO_AUTHORING_GODOT
$previousDatabase=$env:LIBREKO_EDITOR_DB
try {
 $env:LIBREKO_CLIENT_BUILD_MANIFEST=$Manifest
 $env:LIBREKO_AUTHORING_GODOT=[IO.Path]::GetFullPath($GodotEditor)
 $env:LIBREKO_EDITOR_DB=''
 if($EditorExe){& $EditorExe --headless -- --check world-verify-client-output}
 else {& $GodotEditor --headless --path (Join-Path $editorRoot 'src/LibreKO.Editors.Godot') -- --check world-verify-client-output}
 if($LASTEXITCODE -ne 0){throw 'Independent client output verification failed.'}
} finally {
 $env:LIBREKO_CLIENT_BUILD_MANIFEST=$previousManifest
 $env:LIBREKO_AUTHORING_GODOT=$previousGodot
 $env:LIBREKO_EDITOR_DB=$previousDatabase
}
