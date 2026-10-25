$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
dotnet build src/LibreKO.Editors.DataHost -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:NuGetAudit=false --nologo
if($LASTEXITCODE){exit $LASTEXITCODE}
dotnet build src/LibreKO.Editors.Godot -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:NuGetAudit=false --nologo
exit $LASTEXITCODE

