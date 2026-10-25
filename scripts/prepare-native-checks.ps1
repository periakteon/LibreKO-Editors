param([string]$FixtureOwnerRoot='')
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
Set-Location $editorRoot
. (Join-Path $PSScriptRoot 'tool-paths.ps1')
./scripts/test-database.ps1
$mysql=Join-Path (Resolve-MySqlBin) 'mysql.exe'
function FixtureSql([string]$sql){ & $mysql --host=127.0.0.1 --port=33307 --user=root --database=libreko_editor_test_native --execute=$sql; if($LASTEXITCODE){throw 'Native fixture preparation failed'} }
FixtureSql "DROP TRIGGER IF EXISTS editor_native_pause"
FixtureSql "CREATE TRIGGER editor_native_pause BEFORE INSERT ON CollectionRaces FOR EACH ROW SET @editor_delay=IF(NEW.Name='Native interrupted event',SLEEP(3),0)"
FixtureSql 'CREATE DATABASE IF NOT EXISTS libreko_editor_test_schema_native'
FixtureSql 'CREATE TABLE IF NOT EXISTS libreko_editor_test_schema_native.Items(Num int NOT NULL PRIMARY KEY) ENGINE=InnoDB'
FixtureSql 'CREATE DATABASE IF NOT EXISTS libreko_editor_test_eventsonly'
FixtureSql 'CREATE DATABASE IF NOT EXISTS libreko_editor_test_contentonly'
foreach($table in @('TempleEventSchedules','TempleEventRewards','ForgottenTempleWaves','CollectionRaces','CollectionRaceSchedules','CollectionRaceObjectives','CollectionRaceRewards','LotteryEvents','LotterySchedules','LotteryRewards','SiegeWarfare','GameEvents','MapObjects','EventTriggers')){FixtureSql "CREATE TABLE IF NOT EXISTS libreko_editor_test_eventsonly.$table LIKE libreko_editor_test_native.$table"}
foreach($table in @('Items','Characters','Accounts')){FixtureSql "CREATE TABLE IF NOT EXISTS libreko_editor_test_contentonly.$table LIKE libreko_editor_test_native.$table"}
FixtureSql 'CREATE DATABASE IF NOT EXISTS libreko_editor_test_worldonly'
foreach($table in @('ZoneInfos','StartPositions','Homes','Npcs','NpcPositions','NpcItems','MakeItemGroups','MonsterSummons','ItemExchanges','PetLevels','PetTransforms','GameEvents','MapObjects','EventTriggers','Warps')){
 FixtureSql "CREATE TABLE IF NOT EXISTS libreko_editor_test_worldonly.$table LIKE libreko_editor_test_native.$table"
}
FixtureSql 'CREATE DATABASE IF NOT EXISTS libreko_editor_test_worldpartial'
FixtureSql 'CREATE TABLE IF NOT EXISTS libreko_editor_test_worldpartial.ZoneInfos LIKE libreko_editor_test_native.ZoneInfos'
FixtureSql 'CREATE TABLE IF NOT EXISTS libreko_editor_test_worldpartial.Npcs(Id int NOT NULL,IsMonster tinyint(1) NOT NULL,Name longtext NOT NULL,PRIMARY KEY(Id,IsMonster)) ENGINE=InnoDB'
Write-Output 'Prepared isolated native schema and delayed-save fixtures on localhost:33307'

# Every filesystem check uses an alternate profile and a fresh, marked directory.
$fixtureId=[guid]::NewGuid().ToString('N')
if(!$FixtureOwnerRoot){$FixtureOwnerRoot=$editorRoot}
$fixtureRoot=Join-Path ([IO.Path]::GetFullPath($FixtureOwnerRoot)) ".superpowers/sdd/2026-10-23-map-npc-01-foundations/native-fixtures/$fixtureId"
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$sourceRoots=[ordered]@{}
foreach($kind in @('ServerData','ServerMaps','ServerQuests','ClientProject','Pipeline','KoInput','BuildOutput')){
 $selectedPath=Join-Path $fixtureRoot $kind
 New-Item -ItemType Directory -Path $selectedPath | Out-Null
 $sourceRoots[$kind]=$selectedPath
}
$fixtureWorkspace=Join-Path $fixtureRoot 'editor-workspace'
New-Item -ItemType Directory -Path $fixtureWorkspace | Out-Null
$alternateData=Join-Path $fixtureRoot 'ServerDataAlternate'
New-Item -ItemType Directory -Path $alternateData | Out-Null
$alternateRoots=[ordered]@{}
foreach($entry in $sourceRoots.GetEnumerator()){$alternateRoots[$entry.Key]=$entry.Value}
$alternateRoots.ServerData=$alternateData
$alternateId=[guid]::NewGuid().ToString('N')
$sources=@(@{id=$fixtureId;label='Native source fixture one';roots=$sourceRoots},@{id=$alternateId;label='Native source fixture two';roots=$alternateRoots})
$assetRoot=[IO.Path]::GetFullPath((Join-Path $editorRoot '../LibreKO - Official Build with packs'))
$fixtureProfile=Join-Path $fixtureRoot 'profile.json'
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($fixtureProfile,(@{assetRoot=$assetRoot;workspaceRoot=$fixtureWorkspace;sources=$sources;activeSourceId=$fixtureId} | ConvertTo-Json -Depth 12),$utf8)
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'old-profile.json'),(@{assetRoot=$assetRoot;workspaceRoot=$fixtureWorkspace} | ConvertTo-Json),$utf8)
[IO.File]::WriteAllText((Join-Path $sourceRoots.ServerData 'Npcs.json'),'[{"Id":880009,"IsMonster":true,"Name":"Source fixture one"},{"Id":880009,"IsMonster":false,"Name":"Source fixture NPC"}]',$utf8)
[IO.File]::WriteAllText((Join-Path $alternateData 'Npcs.json'),'[{"Id":880009,"IsMonster":true,"Name":"Source fixture two"}]',$utf8)
foreach($name in @('first.json','second.json','unrelated.json')){[IO.File]::WriteAllText((Join-Path $sourceRoots.ServerData $name),'{}',$utf8)}
$fixtureMarker=Join-Path $fixtureRoot 'fixture-marker.json'
[IO.File]::WriteAllText($fixtureMarker,(@{version=1;id=$fixtureId;fixtureRoot=$fixtureRoot;profilePath=$fixtureProfile;sourceProfileId=$fixtureId;alternateId=$alternateId} | ConvertTo-Json),$utf8)
$env:LIBREKO_EDITOR_PROFILE=$fixtureProfile
$env:LIBREKO_EDITOR_SOURCE_FIXTURE=$fixtureMarker
$env:LIBREKO_EDITOR_DB='Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;'
Write-Output "Source fixture marker: $fixtureMarker"
