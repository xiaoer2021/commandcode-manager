$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
[Reflection.Assembly]::LoadFrom((Join-Path $ccRoot 'desktop/app/Host.dll')) | Out-Null
$ccTemp=Join-Path ([IO.Path]::GetTempPath()) ('cmd-usage-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ccTemp | Out-Null
$ccIndex=Join-Path $ccTemp 'index.html'
$ccOriginal='<html><body><script src="zh.js" data-cmd-zh="1"></script><script src="copy.js" data-cmd-copy="1"></script><script src="credits.js" data-cmd-credits="1"></script></body></html>'
[IO.File]::WriteAllText($ccIndex,$ccOriginal)
$ccBundled=Join-Path $ccRoot 'modern/addons'
function Assert-Usage($value,$message){if(-not $value){throw $message}}
for($i=0;$i -lt 2;$i++){
[ModernHost.OfficialUsage]::ApplyAt($ccTemp,$true,$ccBundled)
$ccHtml=[IO.File]::ReadAllText($ccIndex)
Assert-Usage (([regex]::Matches($ccHtml,'data-cmd-manager-credits').Count) -eq 1) 'duplicate usage injection'
Assert-Usage ($ccHtml.Contains('data-cmd-zh') -and $ccHtml.Contains('data-cmd-copy')) 'unrelated feature changed'
Assert-Usage ((Get-ChildItem (Join-Path $ccTemp 'assets') -File).Count -eq 3) 'unexpected addon assets'
[ModernHost.OfficialUsage]::ApplyAt($ccTemp,$false,$ccBundled)
Assert-Usage (-not ([IO.File]::ReadAllText($ccIndex).Contains('data-cmd-manager-credits'))) 'disable failed'
Assert-Usage ([IO.File]::ReadAllText((Join-Path $ccTemp 'assets/manager-credits-state.js')).Contains('false')) 'disable config failed'
}
$ccBefore=[IO.File]::ReadAllText($ccIndex)
try{[ModernHost.OfficialUsage]::ApplyAt($ccTemp,$true,(Join-Path $ccTemp 'missing'));throw 'missing source accepted'}catch{if($_.Exception.Message -eq 'missing source accepted'){throw}}
Assert-Usage ([IO.File]::ReadAllText($ccIndex) -eq $ccBefore) 'missing source changed index'
Assert-Usage ([IO.File]::ReadAllText(($ccIndex+'.manager-credits.bak')) -eq $ccOriginal) 'original backup overwritten'
Write-Output 'PASS: installer enable/disable, idempotence, asset scope, unrelated scripts, missing source, original backup'