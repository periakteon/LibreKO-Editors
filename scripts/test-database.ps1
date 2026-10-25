param([switch]$Stop,[switch]$RunTests,[string]$MySqlBinDirectory='')
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
Set-Location $editorRoot
$dataPath=Join-Path $editorRoot '.superpowers/mysql-data'
. (Join-Path $PSScriptRoot 'tool-paths.ps1')
$bin=if($MySqlBinDirectory){Resolve-MySqlBin -Directory $MySqlBinDirectory}else{Resolve-MySqlBin}
if(!(Test-Path "$bin/mysqld.exe")){throw 'Set the MySQL tools path in this script before running the isolated database tests.'}
$runningData=& "$bin/mysql.exe" --host=127.0.0.1 --port=33307 --user=root --batch --skip-column-names --execute='SELECT @@datadir' 2>$null
$isRunning=$LASTEXITCODE -eq 0
if($isRunning -and [IO.Path]::GetFullPath(($runningData | Select-Object -Last 1).Trim()).TrimEnd('\') -ne [IO.Path]::GetFullPath($dataPath).TrimEnd('\')){throw 'Port 33307 belongs to a different MySQL instance. Refusing to use it.'}
if($Stop){if($isRunning){& "$bin/mysqladmin.exe" --host=127.0.0.1 --port=33307 --user=root shutdown};return}
if(!(Test-Path (Join-Path $dataPath 'mysql'))){New-Item -ItemType Directory -Force -Path $dataPath | Out-Null;& "$bin/mysqld.exe" --no-defaults --initialize-insecure "--datadir=$dataPath" --console;if($LASTEXITCODE){throw 'Test MySQL initialization failed'}}
if(!$isRunning){$logPath=Join-Path $editorRoot '.superpowers/mysql.log';Start-Process -FilePath "$bin/mysqld.exe" -ArgumentList @('--no-defaults',('"--datadir={0}"' -f $dataPath),'--port=33307','--bind-address=127.0.0.1','--mysqlx=0',('"--log-error={0}"' -f $logPath)) -WindowStyle Hidden | Out-Null}
function Invoke-TestSql([string]$Sql){
    $result=& "$bin/mysql.exe" --host=127.0.0.1 --port=33307 --user=root --batch --skip-column-names "--execute=$Sql" 2>$null
    if($LASTEXITCODE){throw 'Isolated test schema query failed.'}
    return $result
}
$deadline=[DateTime]::UtcNow.AddSeconds(15)
do {
    $readyData=& "$bin/mysql.exe" --host=127.0.0.1 --port=33307 --user=root --batch --skip-column-names --execute='SELECT @@datadir' 2>$null
    if($LASTEXITCODE -eq 0){break}
    if([DateTime]::UtcNow -gt $deadline){throw 'Isolated MySQL did not start.'}
    Start-Sleep -Milliseconds 200
} while($true)
if([IO.Path]::GetFullPath(($readyData | Select-Object -Last 1).Trim()).TrimEnd('\') -ne [IO.Path]::GetFullPath($dataPath).TrimEnd('\')){throw 'Refusing to align a database outside the editor-owned datadir.'}
# This fixed test database is disposable editor infrastructure, never a profile connection.
# EnsureCreated does not update existing tables after the referenced game model changes.
$testSchema='libreko_editor_test_native'
$columns=@(
    @{Table='Characters';Column='RankMarks';Definition='longblob NOT NULL'}
)
foreach($table in @('TempleEventSchedules','CollectionRaceSchedules','LotterySchedules')){
    $columns+=@{Table=$table;Column='Repeat';Definition='tinyint unsigned NOT NULL DEFAULT 0'}
    $columns+=@{Table=$table;Column='Date';Definition='date NULL'}
    $columns+=@{Table=$table;Column='DayOfMonth';Definition='tinyint unsigned NOT NULL DEFAULT 0'}
}
foreach($column in $columns){
    $table=$column.Table;$name=$column.Column
    $tableExists=Invoke-TestSql "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='$testSchema' AND TABLE_NAME='$table'"
    if([int]$tableExists -eq 0){continue}
    $columnExists=Invoke-TestSql "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='$testSchema' AND TABLE_NAME='$table' AND COLUMN_NAME='$name'"
    if([int]$columnExists -eq 0){
        Invoke-TestSql ('ALTER TABLE `{0}`.`{1}` ADD COLUMN `{2}` {3}' -f $testSchema,$table,$name,$column.Definition) | Out-Null
        if($name -eq 'Repeat'){Invoke-TestSql ('UPDATE `{0}`.`{1}` SET `Repeat`=1 WHERE `Day` IS NOT NULL' -f $testSchema,$table) | Out-Null}
        Write-Output "Aligned isolated test column: $table.$name"
    }
}
if($RunTests){dotnet test tests/LibreKO.Editors.DataHost.Tests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:NuGetAudit=false;if($LASTEXITCODE){throw 'Data tests failed'}}

