param([string]$OutputDirectory='',[string]$GodotEditor='')
$ErrorActionPreference='Stop'
$editorRoot=Split-Path $PSScriptRoot
Set-Location $editorRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $editorRoot 'artifacts/LibreKO-Editors'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
dotnet publish src/LibreKO.Editors.DataHost -c Release -r win-x64 -o "$OutputDirectory/data" --self-contained true -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false -v minimal
if($LASTEXITCODE){throw 'Data host publication failed'}
. (Join-Path $PSScriptRoot 'tool-paths.ps1')
$engine=if($GodotEditor){Resolve-GodotEditor -Path $GodotEditor}else{Resolve-GodotEditor}
$project=Join-Path $editorRoot 'src/LibreKO.Editors.Godot'
$stdout=Join-Path $editorRoot '.superpowers/export-stdout.log'
$stderr=Join-Path $editorRoot '.superpowers/export-stderr.log'
New-Item -ItemType Directory -Force -Path (Split-Path $stdout) | Out-Null
$exportProc=Start-Process -FilePath $engine -ArgumentList @('--headless','--editor','--path',('"{0}"' -f $project),'--export-release','"Windows Desktop"',('"{0}"' -f "$OutputDirectory/LibreKO.Editors.exe"),'--quit') -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$deadline=[DateTime]::UtcNow.AddMinutes(3)
$completedAt=$null
$completedShutdownFallback=$false
while(!$exportProc.WaitForExit(1000)){
    $text=(Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue) -replace '\x1B\[[0-9;]*m',''
    if($text -match '\[ DONE \] savepack'){
        if(!$completedAt){$completedAt=[DateTime]::UtcNow}
        if(([DateTime]::UtcNow-$completedAt).TotalSeconds -ge 8){
            $package=Get-Item -LiteralPath "$OutputDirectory/LibreKO.Editors.pck"
            if(([DateTime]::UtcNow-$package.LastWriteTimeUtc).TotalSeconds -ge 8){
                Stop-Process -Id $exportProc.Id
                $completedShutdownFallback=$true
                break
            }
        }
    }
    if([DateTime]::UtcNow -gt $deadline){Stop-Process -Id $exportProc.Id;throw 'Godot export timed out'}
}
$exportText=((Get-Content -LiteralPath $stdout -Raw)+(Get-Content -LiteralPath $stderr -Raw)) -replace '\x1B\[[0-9;]*m',''
if($exportText -match '(?m)^ERROR:' -or $exportText -notmatch '\[ DONE \] savepack'){throw 'Godot publication failed; inspect .superpowers/export-stdout.log and export-stderr.log'}
if(!$completedShutdownFallback -and $exportProc.ExitCode -ne 0){throw 'Godot exited with an export error'}
New-Item -ItemType Directory -Force -Path "$OutputDirectory/profiles" | Out-Null
. (Join-Path $PSScriptRoot 'profile-publication.ps1')
$profileSource=if(Test-Path -LiteralPath "$OutputDirectory/profiles/local.json"){"$OutputDirectory/profiles/local.json"}elseif(Test-Path -LiteralPath 'profiles/local.json'){'profiles/local.json'}else{$null}
$exportProfile=Get-PublicationProfile -EditorRoot $editorRoot -ProfileSource $profileSource
$exportProfile | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath "$OutputDirectory/profiles/local.json" -Encoding utf8

Copy-Item -LiteralPath LICENSE,THIRD_PARTY_NOTICES.md,LibreKO-LICENSE,LibreKO-SERVER-NOTICES.md -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $editorRoot 'licenses/Godot-LICENSE.txt') -Destination "$OutputDirectory/GODOT-LICENSE.txt"
New-Item -ItemType Directory -Force -Path "$OutputDirectory/THIRD-PARTY-LICENSES" | Out-Null
Get-ChildItem -LiteralPath (Join-Path $editorRoot 'licenses') -File -Force | Copy-Item -Destination "$OutputDirectory/THIRD-PARTY-LICENSES"
$publishedReadme=[IO.File]::ReadAllText((Join-Path $editorRoot 'docs/distribution-README.md'))
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'),$publishedReadme,[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Force -Path "$OutputDirectory/help" | Out-Null
Copy-Item -Path (Join-Path $editorRoot 'docs/help/*.md') -Destination "$OutputDirectory/help"
Copy-Item -LiteralPath (Join-Path $editorRoot 'docs/public-release-2026.10.25.md') -Destination "$OutputDirectory/RELEASE-NOTES.md"
Copy-Item -LiteralPath (Join-Path $editorRoot 'docs/map-npc-authoring.md') -Destination "$OutputDirectory/MAP-NPC-AUTHORING.md"
if($completedShutdownFallback){Write-Output 'Export completed; closed the idle Godot exporter after its pack finished writing. The packaged executable still requires verification.'}
Write-Output "Desktop application: $OutputDirectory/LibreKO.Editors.exe"
