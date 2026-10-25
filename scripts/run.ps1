param([string]$Check='',[switch]$Screenshot,[switch]$Render,[string]$GodotEditor='')
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot 'tool-paths.ps1')
$engine=if($GodotEditor){Resolve-GodotEditor -Path $GodotEditor}else{Resolve-GodotEditor}
$engineArgs=@('--path',"$editorRoot/src/LibreKO.Editors.Godot")
if($Check){if(!$Render){$engineArgs+='--headless'};$engineArgs+=@('--','--check',$Check)}
if($Screenshot){$engineArgs+=@('--','--screenshot')}
& $engine @engineArgs
exit $LASTEXITCODE

