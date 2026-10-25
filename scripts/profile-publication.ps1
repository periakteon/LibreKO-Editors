function Get-PublicationProfile {
 param([Parameter(Mandatory)][string]$EditorRoot,[string]$ProfileSource='')
 $exportProfile=@{assetRoot=(Join-Path (Split-Path $EditorRoot) 'LibreKO - Official Build with packs');workspaceRoot=(Join-Path $EditorRoot 'workspace');connectionEnvironmentVariable='LIBREKO_EDITOR_DB'}
 if($ProfileSource){$savedProfile=Get-Content -LiteralPath $ProfileSource -Raw | ConvertFrom-Json -AsHashtable;if($savedProfile -isnot [System.Collections.IDictionary]){throw 'Saved editor profile must be a JSON object'};foreach($key in $savedProfile.Keys){$exportProfile[$key]=$savedProfile[$key]}}
 return $exportProfile
}
