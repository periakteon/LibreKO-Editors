param([string]$ApplicationDirectory='', [string]$Check='boot', [switch]$WithoutGlobalDotnet, [switch]$Render,[ValidateRange(15,1800)][int]$TimeoutSeconds=180)
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
if(!$ApplicationDirectory){$ApplicationDirectory=Join-Path $editorRoot 'artifacts/LibreKO-Editors'}
$exe=Join-Path $ApplicationDirectory 'LibreKO.Editors.exe'
$log=Join-Path $editorRoot ".superpowers/release-$Check.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
if(!(Test-Path $exe)){throw 'Publish the editor before verifying it'}
$oldDotnetRoot=$env:DOTNET_ROOT
$oldDotnetX64=$env:DOTNET_ROOT_X64
$oldLookup=$env:DOTNET_MULTILEVEL_LOOKUP
$oldPath=$env:PATH
try {
 if($WithoutGlobalDotnet){$env:DOTNET_ROOT='C:/editor-no-global-dotnet';$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT;$env:DOTNET_MULTILEVEL_LOOKUP='0';$env:PATH="$env:SystemRoot/System32"}
 $verifyArgs=@('--log-file',('"{0}"' -f $log),'--','--check',$Check)
 if(!$Render){$verifyArgs=@('--headless')+$verifyArgs}
 $startedAt=[DateTime]::UtcNow
 $releaseProcess=Start-Process -FilePath $exe -ArgumentList $verifyArgs -WindowStyle Hidden -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
 while(!$releaseProcess.WaitForExit(1000)){if([DateTime]::UtcNow -gt $deadline){$releaseProcess.Kill($true);$releaseProcess.WaitForExit();throw 'Packaged editor check timed out'}}
 if((Get-Item -LiteralPath $log).LastWriteTimeUtc -lt $startedAt.AddSeconds(-1)){throw 'Packaged editor did not produce a fresh check log'}
 $text=Get-Content -LiteralPath $log -Raw
 if($releaseProcess.ExitCode -ne 0 -or $text -notmatch "CHECK $Check PASS" -or $text -match '(?m)^(ERROR:|SHADER ERROR:)' ){throw "Packaged editor failed $Check; inspect $log"}
 Write-Output "Packaged Windows editor: CHECK $Check PASS"
} finally {$env:DOTNET_ROOT=$oldDotnetRoot;$env:DOTNET_ROOT_X64=$oldDotnetX64;$env:DOTNET_MULTILEVEL_LOOKUP=$oldLookup;$env:PATH=$oldPath}
