#Requires -Version 7.2
function Resolve-GodotEditor {
    param([string]$Path=$env:LIBREKO_GODOT)
    if(!$Path){
        foreach($name in @('godot-mono','godot','Godot')){
            $command=Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if($command){$Path=$command.Source;break}
        }
    }
    if(!$Path -or !(Test-Path -LiteralPath $Path -PathType Leaf)){throw 'Select Godot 4.7.2 Mono with -GodotEditor or LIBREKO_GODOT, or add it to PATH.'}
    return [IO.Path]::GetFullPath($Path)
}
function Resolve-MySqlBin {
    param([string]$Directory=$env:LIBREKO_MYSQL_BIN)
    if(!$Directory){
        $command=Get-Command mysqld.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if($command){$Directory=Split-Path $command.Source}
    }
    if(!$Directory){throw 'Set LIBREKO_MYSQL_BIN or -MySqlBinDirectory to the directory containing mysql.exe, mysqladmin.exe and mysqld.exe.'}
    foreach($name in @('mysql.exe','mysqladmin.exe','mysqld.exe')){
        if(!(Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf)){throw "Missing MySQL tool $name. Set LIBREKO_MYSQL_BIN to a complete tools directory."}
    }
    return [IO.Path]::GetFullPath($Directory)
}
