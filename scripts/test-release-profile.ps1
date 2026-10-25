$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
$fixtureRoot=Join-Path $editorRoot ('.superpowers/publication-fixtures/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$inputPath=Join-Path $fixtureRoot 'local.json'
$source=@{assetRoot='C:/custom client';workspaceRoot='C:/custom editor history';connectionEnvironmentVariable='CUSTOM_DB';serverScheduleTimeZone='Europe/Istanbul';activeSourceId='custom';sources=@(@{id='custom';label='Custom fork';roots=@{ClientProject='C:/custom/client';ServerMaps='C:/custom/maps'};terrainAliases=@{'1'='customterrain'};tools=@{godotEditor='C:/tools/Godot.exe';dotnetSdk='C:/tools/dotnet.exe';exportTemplate='C:/tools/template.exe'}});future=@{nested=@{preserve='yes'}}}
$source|ConvertTo-Json -Depth 32|Set-Content -LiteralPath $inputPath
$before=Get-FileHash -LiteralPath $inputPath
. (Join-Path $PSScriptRoot 'profile-publication.ps1')
$actual=Get-PublicationProfile -EditorRoot $editorRoot -ProfileSource $inputPath
$roundtrip=$actual|ConvertTo-Json -Depth 64|ConvertFrom-Json
if($roundtrip.activeSourceId -ne 'custom' -or $roundtrip.sources[0].tools.exportTemplate -ne 'C:/tools/template.exe' -or $roundtrip.sources[0].terrainAliases.'1' -ne 'customterrain' -or $roundtrip.future.nested.preserve -ne 'yes' -or $roundtrip.workspaceRoot -ne 'C:/custom editor history'){throw 'Publication dropped source/tool/profile/history selections'}
if((Get-FileHash -LiteralPath $inputPath).Hash -ne $before.Hash){throw 'Profile preparation rewrote original settings'}
$default=Get-PublicationProfile -EditorRoot $editorRoot
if(!$default.assetRoot -or !$default.workspaceRoot -or $default.connectionEnvironmentVariable -ne 'LIBREKO_EDITOR_DB'){throw 'Default profile is incomplete'}
Write-Output 'Publication profile preservation PASS'
