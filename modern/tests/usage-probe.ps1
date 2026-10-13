$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path (Get-Location) 'desktop/app/Host.dll'))|Out-Null
$ccStore=Get-Content "$env:APPDATA\commandcode-account-manager\accounts-native.json" -Raw|ConvertFrom-Json
$ccAccount=$ccStore.Accounts[0]
$ccKey=[CommandCodeManager.Core]::Decrypt($ccAccount.Secret)
$ccOrg=[string]$ccAccount.Data.OrgId
$ccQuery=if($ccOrg){'orgId='+[uri]::EscapeDataString($ccOrg)+'&'}else{''}
$ccTask=[CommandCodeManager.Core]::Get($ccKey,'/alpha/usage/summary',$ccQuery)
$ccSummary=$ccTask.GetAwaiter().GetResult()
Write-Output ('SummaryFields='+($ccSummary.Keys -join ','))
foreach($ccField in 'totalTokens','totalTokensIn','totalTokensOut','totalCount'){Write-Output ($ccField+'='+$ccSummary[$ccField])}
$ccTask=[CommandCodeManager.Core]::Get($ccKey,'/alpha/usage/summary',$ccQuery+'since=2026-10-01T00%3A00%3A00Z&until=2026-10-01T00%3A00%3A00Z')
$ccProbe=$ccTask.GetAwaiter().GetResult()
Write-Output ('ZeroWindowCount='+$ccProbe['totalCount'])
Write-Output ('PeriodBasis='+$ccSummary['periodBasis'])
$ccTask=[CommandCodeManager.Core]::Get($ccKey,'/alpha/usage/summary',$ccQuery+'since=1970-01-01T00%3A00%3A00Z')
$ccAll=$ccTask.GetAwaiter().GetResult()
Write-Output ('AllCount='+$ccAll['totalCount']+';AllTokens='+$ccAll['totalTokens']+';Basis='+$ccAll['periodBasis'])
$ccKey=$null