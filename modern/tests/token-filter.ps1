$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path (Get-Location) 'desktop/app/Host.dll'))|Out-Null
$ccStore=Get-Content "$env:APPDATA\commandcode-account-manager\accounts-native.json" -Raw|ConvertFrom-Json
$ccAccount=$ccStore.Accounts[2]
$ccKey=[CommandCodeManager.Core]::Decrypt($ccAccount.Secret)
$ccQuery='orgId='+[uri]::EscapeDataString([string]$ccAccount.Data.OrgId)+'&'
$ccFuture=[CommandCodeManager.Core]::Get($ccKey,'/alpha/usage/summary',$ccQuery+'since=2030-01-01T00%3A00%3A00Z').GetAwaiter().GetResult()
if($ccFuture['totalTokens'] -ne 0){throw 'API since filter is not supported'}
Write-Output 'PASS: future since returns zero, date filter verified'
$ccRows=[ModernHost.TokenHistory]::Fetch($ccKey,[string]$ccAccount.Data.OrgId,'day',0).GetAwaiter().GetResult()
$ccRows|ForEach-Object{Write-Output ($_.Start.Substring(0,10)+' tokens='+$_.Tokens+' input='+$_.Input+' output='+$_.Output+' error='+$_.Error)}
$ccKey=$null