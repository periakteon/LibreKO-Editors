param([string]$FixtureOwnerRoot='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'prepare-native-checks.ps1') -FixtureOwnerRoot $FixtureOwnerRoot
$sourceMarker=Get-Content -LiteralPath $env:LIBREKO_EDITOR_SOURCE_FIXTURE -Raw|ConvertFrom-Json
$sourceFixtureRoot=[IO.Path]::GetFullPath($sourceMarker.fixtureRoot)
$sourceFixtureBase=[IO.Path]::GetFullPath((Join-Path $FixtureOwnerRoot '.superpowers/sdd/2026-10-23-map-npc-01-foundations/native-fixtures'))
if([IO.Path]::GetDirectoryName($sourceFixtureRoot) -ne $sourceFixtureBase -or [IO.Path]::GetFileName($sourceFixtureRoot) -ne $sourceMarker.id){throw 'Source fixture ownership mismatch'}
$sourceUtf8=[Text.UTF8Encoding]::new($false)
$sourceRows=@{
 ZoneInfos=@{ZoneNo=210;MapName='Fixture source zone';SmdName='native-world.smd'}
 StartPositions=@{ZoneId=210};Homes=@{Nation=1};NpcPositions=@{Index=999;NpcId=880009;ZoneId=210;ActType=1}
 NpcItems=@{Index=880009;IsMonster=$true};MakeItemGroups=@{GroupNum=880009;Items='123450001,123450001,123450002'}
 MonsterSummons=@{Index=880009};ItemExchanges=@{Index=880009};PetLevels=@{Level=1;Exp=9007199254740993L}
 PetTransforms=@{Id=880009};GameEvents=@{ZoneNum=210;EventNum=1};MapObjects=@{Id=880009;ZoneId=210};EventTriggers=@{Index=880009};Warps=@{WarpId=1;ZoneId=210}
 ItemSells=@{SellingGroup=880009;Line=0;Index=0;ItemId=123450001};ItemExchangeExps=@{Index=880009;RandomFlag=0}
 MiningExchanges=@{Index=1;NpcId=1;GiveEffect=0;OreType=0;OriginItemNum=123450001;GiveItemNum=123450001;GiveItemCount=1;SuccessRate=10000}
 MiningFishingItems=@{Index=1;Type=0;WarStatus=0;UseItemType=0;GiveItemNum=123450001;GiveItemCount=1;SuccessRate=10000}
}
foreach($entry in $sourceRows.GetEnumerator()){
 $sourceTarget=Join-Path $sourceFixtureRoot ('ServerData/'+$entry.Key+'.json')
 [IO.File]::WriteAllText($sourceTarget,('['+($entry.Value|ConvertTo-Json -Compress)+']'),$sourceUtf8)
}
[IO.File]::WriteAllText((Join-Path $sourceFixtureRoot 'ServerQuests/quest-manifest.json'),'["native.quest"]',$sourceUtf8)
[IO.File]::WriteAllText((Join-Path $sourceFixtureRoot 'ServerData/Items.json'),'[{"Num":123450001,"Name":"Fixture blade","Countable":0},{"Num":123450002,"Name":"Second fixture blade","Countable":0},{"Num":123450003,"Name":"Third fixture blade","Countable":0}]',$sourceUtf8)
[IO.File]::WriteAllText((Join-Path $sourceFixtureRoot 'ServerQuests/native.quest'),"Bind Npc 880009`nOn greeting`n    Say `"Source fixture`"`n    Topic `"Close`" goto close`n",$sourceUtf8)
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot) '../LibreKO/Server/LibreKO.Game/Map/clanfight_b.smd') -Destination (Join-Path $sourceFixtureRoot 'ServerMaps/native-world.smd')
Write-Output "Prepared server source fixtures: $sourceFixtureRoot"
