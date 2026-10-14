$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ccHost=Join-Path $ccRoot 'desktop/app/Host.dll'
[Reflection.Assembly]::LoadFrom($ccHost)|Out-Null
$ccNow=[DateTimeOffset]::Parse('2030-01-01T00:00:00+08:00')
$ccAccount=New-Object CommandCodeManager.Account
$ccAccount.RenewalAt=$ccNow.AddDays(10).ToString('o')
$ccSettings=New-Object CommandCodeManager.Settings
if([CommandCodeManager.Core]::Alerts($ccAccount,$ccSettings,$ccNow).Count -ne 0){throw 'Premature reminder'}
foreach($ccDays in @(7,3,1,0)) {
    $ccAlert=[CommandCodeManager.Core]::Alerts($ccAccount,$ccSettings,$ccNow.AddDays(10-$ccDays))
    if($ccAlert.Count -ne 1 -or -not $ccAlert[0].Id.StartsWith("renewal:$($ccDays):")){throw 'Reminder boundary failed'}
}
$ccAccount.Error='offline'
if([CommandCodeManager.Core]::Alerts($ccAccount,$ccSettings,$ccNow.AddDays(9)).Count -ne 1){throw 'Offline reminder lost'}
$ccJson=New-Object Web.Script.Serialization.JavaScriptSerializer
$ccSaved=$ccJson.Deserialize($ccJson.Serialize($ccAccount),[CommandCodeManager.Account])
if($ccSaved.RenewalAt -ne $ccAccount.RenewalAt){throw 'Local date persistence failed'}
$ccPortable=New-Object ModernHost.PortableAccount
$ccPortable.Name='fixture';$ccPortable.ApiKey='fixture-only';$ccPortable.RenewalAt=$ccAccount.RenewalAt
$ccEncrypted=[ModernHost.AccountTransfer]::Encrypt(@($ccPortable),'fixture-password')
$ccImported=[ModernHost.AccountTransfer]::Decrypt($ccEncrypted,'fixture-password')
if($ccImported[0].RenewalAt -ne $ccAccount.RenewalAt){throw 'Portable renewal lost'}
$ccAccount.RenewalAt=''
if([CommandCodeManager.Core]::Alerts($ccAccount,$ccSettings,$ccNow.AddDays(9)).Count -ne 0){throw 'Cleared date still alerts'}
Write-Output 'PASS renewal 7/3/1-day boundaries, overdue, offline, persistence, encrypted transfer and removal'
