$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path (Get-Location) 'desktop/app/Host.dll'))|Out-Null
function Assert-Token($value,$message){if(-not $value){throw $message}}
$ccNow=[DateTimeOffset]::Parse('2026-10-13T10:00:00Z')
$ccDays=[ModernHost.TokenHistory]::Boundaries('day',0,$ccNow)
Assert-Token ($ccDays.Count -eq 8 -and $ccDays[0].ToString('yyyy-MM-dd HH:mm zzz') -eq '2026-10-07 00:00 +08:00') 'day boundary wrong'
$ccWeeks=[ModernHost.TokenHistory]::Boundaries('week',0,$ccNow)
Assert-Token ($ccWeeks.Count -eq 7 -and $ccWeeks[5].ToString('yyyy-MM-dd') -eq '2026-10-12') 'week does not start Monday'
$ccMonths=[ModernHost.TokenHistory]::Boundaries('month',0,$ccNow)
Assert-Token ($ccMonths[0].ToString('yyyy-MM-dd') -eq '2026-05-01' -and $ccMonths[6].ToString('yyyy-MM-dd') -eq '2026-11-01') 'month boundary wrong'
$ccPrevious=[ModernHost.TokenHistory]::Boundaries('day',1,$ccNow)
Assert-Token ($ccPrevious[7] -eq $ccDays[0]) 'page gap'
$ccOlder=[Collections.Generic.Dictionary[string,object]]::new();$ccOlder.Add('totalTokens',100.0)
$ccNewer=[Collections.Generic.Dictionary[string,object]]::new();$ccNewer.Add('totalTokens',35.0)
Assert-Token ([ModernHost.TokenHistory]::Difference($ccOlder,$ccNewer,'totalTokens') -eq 65) 'difference wrong'
Assert-Token ($null -eq [ModernHost.TokenHistory]::Difference($ccNewer,$ccOlder,'totalTokens')) 'negative shown as valid'
Assert-Token ($null -eq [ModernHost.TokenHistory]::Difference($ccOlder,$ccNewer,'totalCost')) 'missing shown as zero'
Write-Output 'PASS: Hong Kong days, Monday weeks, calendar months, contiguous paging, negative and missing data'
$ccStore=Get-Content "$env:APPDATA\commandcode-account-manager\accounts-native.json" -Raw|ConvertFrom-Json
foreach($ccAccount in $ccStore.Accounts){
$ccKey=[CommandCodeManager.Core]::Decrypt($ccAccount.Secret)
$ccData=[CommandCodeManager.Core]::Fetch($ccKey).GetAwaiter().GetResult()
Write-Output ('LIVE totals='+$ccData.TotalTokens+';input='+$ccData.InputTokens+';output='+$ccData.OutputTokens+';error='+$ccData.TokenError)
}
$ccAccount=$ccStore.Accounts[0];$ccKey=[CommandCodeManager.Core]::Decrypt($ccAccount.Secret)
foreach($ccGrain in 'day','week','month'){
$ccRows=[ModernHost.TokenHistory]::Fetch($ccKey,[string]$ccAccount.Data.OrgId,$ccGrain,0).GetAwaiter().GetResult()
$ccValid=@($ccRows|Where-Object {$null -ne $_.Tokens -and -not $_.Error})
Write-Output ('LIVE '+$ccGrain+' rows='+$ccRows.Count+';valid='+$ccValid.Count+';sum='+($ccValid|Measure-Object Tokens -Sum).Sum)
Write-Output ('ErrorSample='+$ccRows[0].Error+';Tokens='+$ccRows[0].Tokens);foreach($ccRow in $ccValid){Assert-Token ($ccRow.Tokens -ge 0) 'negative live tokens'}
}
$ccKey=$null