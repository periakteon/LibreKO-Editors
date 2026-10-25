param([string]$ApplicationDirectory='', [string[]]$Modes=@())
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
Set-Location $editorRoot
if(!$ApplicationDirectory){$ApplicationDirectory=Join-Path $editorRoot 'artifacts/LibreKO-Editors'}
if(!(Test-Path -LiteralPath (Join-Path $ApplicationDirectory 'LibreKO.Editors.exe'))){throw 'Publish the editor first'}
$headless=@('boot','content','effects-diagnostic','compatibility','compatibility-cancel','compatibility-writes','compatibility-schema','compatibility-content','item-editor','character-editor','account-editor','inventory','inventory-add','references','shutdown-pending','shell-navigation','shell-shutdown','shell-compatibility','shell-refresh','events-editor','events-drafts','events-history','events-world','events-cancel-recover','events-helper-exit','events-focused-save','events-child-duplicate','events-no-assets','named-references','source-profiles','file-recovery','world-editor','world-drafts','world-history','world-json','world-seed-compare','world-file-apply','world-source-cancel','world-quests','world-mixed-save','world-authoring-shutdown','world-client-build')
$gpu=@('shell-gallery','events-gallery','studio-gallery','inventory-style','effects','compatibility-gallery','world-spatial','world-gallery','world-smd','world-terrain','world-scenery','world-npc-visuals','world-source-preview')
$all=@($headless)+@($gpu)
if(!$Modes.Count){$Modes=$all}
foreach($mode in $Modes){if($mode -notin $all){throw "Unknown release mode: $mode"}}
$runRoot=Join-Path $editorRoot ('.superpowers/release-map-npc/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$oldProfile=$env:LIBREKO_EDITOR_PROFILE;$oldMarker=$env:LIBREKO_EDITOR_SOURCE_FIXTURE;$oldDb=$env:LIBREKO_EDITOR_DB;$oldGodot=$env:LIBREKO_AUTHORING_GODOT
$results=@();$clientProfile=$null;$clientMarker=$null
try {
 . ./scripts/prepare-client-checks.ps1 -FixtureOwnerRoot $ApplicationDirectory *> (Join-Path $runRoot 'prepare.log')
 $regularProfile=$env:LIBREKO_EDITOR_PROFILE;$regularMarker=$env:LIBREKO_EDITOR_SOURCE_FIXTURE
 foreach($mode in $Modes){
  if($mode -eq 'named-references'){. ./scripts/prepare-native-checks.ps1 -FixtureOwnerRoot $ApplicationDirectory *> (Join-Path $runRoot ('prepare-'+$mode+'.log'))}
  elseif($mode -match '^(world-|source-profiles|file-recovery)'){
   if($mode -eq 'world-source-preview' -and $clientProfile){$env:LIBREKO_EDITOR_PROFILE=$clientProfile;$env:LIBREKO_EDITOR_SOURCE_FIXTURE=$clientMarker}
   else {. ./scripts/prepare-client-checks.ps1 -FixtureOwnerRoot $ApplicationDirectory *> (Join-Path $runRoot ('prepare-'+$mode+'.log'))}
  }else {$env:LIBREKO_EDITOR_PROFILE=$regularProfile;$env:LIBREKO_EDITOR_SOURCE_FIXTURE=$regularMarker}
  $env:LIBREKO_EDITOR_DB='Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;'
  if($mode -eq 'compatibility-schema'){$env:LIBREKO_EDITOR_DB='Server=127.0.0.1;Port=33307;Database=libreko_editor_test_schema_native;User=root;Pooling=false;'}
  if($mode -in @('compatibility-content','events-no-assets')){
   $missing=Get-Content -LiteralPath $regularProfile -Raw|ConvertFrom-Json;$missing.assetRoot=Join-Path $runRoot 'missing-assets'
   $missingPath=Join-Path $runRoot ('profile-'+$mode+'.json');$missing|ConvertTo-Json -Depth 32|Set-Content -LiteralPath $missingPath;$env:LIBREKO_EDITOR_PROFILE=$missingPath
  }
  $marker=Get-Content -LiteralPath $env:LIBREKO_EDITOR_SOURCE_FIXTURE -Raw|ConvertFrom-Json
  if($marker.fixtureRoot -notlike "$editorRoot*.superpowers*" -or !(Test-Path -LiteralPath $env:LIBREKO_EDITOR_PROFILE)){throw 'Fixture profile/root prerequisite missing'}
  $start=[DateTimeOffset]::UtcNow
  ./scripts/verify-release.ps1 -ApplicationDirectory $ApplicationDirectory -Check $mode -WithoutGlobalDotnet:($mode -notin $gpu) -Render:($mode -in $gpu) -TimeoutSeconds $(if($mode -in @('world-client-build','world-source-preview','world-authoring-shutdown')){900}else{180})
  Copy-Item -LiteralPath (Join-Path $editorRoot ".superpowers/release-$mode.log") -Destination (Join-Path $runRoot "$mode.log")
  $results+=@{mode=$mode;status='PASS';gpu=($mode -in $gpu);withoutGlobalDotnet=($mode -notin $gpu);startedAt=$start;completedAt=[DateTimeOffset]::UtcNow;profile=$env:LIBREKO_EDITOR_PROFILE;marker=$env:LIBREKO_EDITOR_SOURCE_FIXTURE}
  $results|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $runRoot 'results.json')
  if($mode -eq 'world-client-build'){$clientProfile=$env:LIBREKO_EDITOR_PROFILE;$clientMarker=$env:LIBREKO_EDITOR_SOURCE_FIXTURE}
 }
 Write-Output "Map/NPC packaged verification: $($results.Count)/$($Modes.Count) PASS; evidence $runRoot"
}finally {$env:LIBREKO_EDITOR_PROFILE=$oldProfile;$env:LIBREKO_EDITOR_SOURCE_FIXTURE=$oldMarker;$env:LIBREKO_EDITOR_DB=$oldDb;$env:LIBREKO_AUTHORING_GODOT=$oldGodot}
