param([Parameter(Mandatory)][string]$ApplicationDirectory,[Parameter(Mandatory)][string]$ZipPath)
$ErrorActionPreference='Stop'
$applicationRoot=[IO.Path]::GetFullPath($ApplicationDirectory)
$zipFile=[IO.Path]::GetFullPath($ZipPath)
if(Test-Path -LiteralPath $zipFile){throw 'Choose a new ZIP path; existing releases are preserved.'}
$runtimePaths=@('LibreKO.Editors.exe','LibreKO.Editors.pck','data','data_LibreKO.Editors.Godot_windows_x86_64','help','README.md','LICENSE','THIRD_PARTY_NOTICES.md','THIRD-PARTY-LICENSES','GODOT-LICENSE.txt','LibreKO-LICENSE','LibreKO-SERVER-NOTICES.md','RELEASE-NOTES.md','MAP-NPC-AUTHORING.md')
$files=@()
foreach($relative in $runtimePaths){
    $item=Get-Item -LiteralPath (Join-Path $applicationRoot $relative)
    $entries=@($item)
    if($item.PSIsContainer){$entries+=@(Get-ChildItem -LiteralPath $item.FullName -Recurse -Force)}
    foreach($entry in $entries){
        if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Release input contains a link: $($entry.FullName)"}
        if(!$entry.PSIsContainer){$files+=$entry.FullName}
    }
}
New-Item -ItemType Directory -Force -Path (Split-Path $zipFile) | Out-Null
$archive=[IO.Compression.ZipFile]::Open($zipFile,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in $files){
        $relative=[IO.Path]::GetRelativePath($applicationRoot,$file).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {$archive.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($zipFile)
try {
    if($archive.Entries.Count -ne $files.Count){throw 'Incomplete release ZIP.'}
    foreach($entry in $archive.Entries){
        $top=$entry.FullName.Split('/')[0]
        if($top -notin $runtimePaths -or $entry.FullName.Contains('..')){throw 'Unexpected entry in release ZIP.'}
    }
} finally {$archive.Dispose()}
Write-Output "Portable release: $zipFile ($($files.Count) files; profiles, workspaces and scratch excluded)"
Get-FileHash -LiteralPath $zipFile -Algorithm SHA256
