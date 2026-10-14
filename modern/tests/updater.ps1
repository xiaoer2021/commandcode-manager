$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
[Reflection.Assembly]::LoadFrom((Join-Path $ccRoot 'desktop/app/Host.dll'))|Out-Null
if(-not [ModernHost.AppUpdater]::Newer('1.1.0','1.0.1')){throw 'New release not detected'}
if([ModernHost.AppUpdater]::Newer('1.0.1','1.1.0')){throw 'Downgrade offered'}
if([ModernHost.AppUpdater]::Newer('bad','1.0.1')){throw 'Invalid version accepted'}
if([ModernHost.AppUpdater]::Newer('1.1.0','1.1.0')){throw 'Same version offered'}
$ccUrl='https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.1.0/CommandCode-Manager-Setup-1.1.0.exe'
if(-not [ModernHost.AppUpdater]::Trusted($ccUrl,'1.1.0','CommandCode-Manager-Setup-1.1.0.exe')){throw 'Own release rejected'}
if([ModernHost.AppUpdater]::Trusted($ccUrl.Replace('xiaoer2021','attacker'),'1.1.0','CommandCode-Manager-Setup-1.1.0.exe')){throw 'Wrong repository accepted'}
if([ModernHost.AppUpdater]::Trusted($ccUrl.Replace('https:','http:'),'1.1.0','CommandCode-Manager-Setup-1.1.0.exe')){throw 'HTTP accepted'}
$ccUpdater=New-Object ModernHost.AppUpdater
$ccRejected=$false
try{$ccUpdater.Install()}catch{$ccRejected=$true}
if(-not $ccRejected){throw 'Unverified installation accepted'}
$ccFile=Join-Path ([IO.Path]::GetTempPath()) ('cc-update-check-'+[guid]::NewGuid().ToString('N'))
try {
    [IO.File]::WriteAllText($ccFile,'installer fixture')
    $ccHash=(Get-FileHash -LiteralPath $ccFile -Algorithm SHA256).Hash
    if(-not [ModernHost.AppUpdater]::VerifyFile($ccFile,$ccHash)){throw 'Valid checksum rejected'}
    [IO.File]::AppendAllText($ccFile,'modified')
    if([ModernHost.AppUpdater]::VerifyFile($ccFile,$ccHash)){throw 'Tampered file accepted'}
} finally {if(Test-Path -LiteralPath $ccFile){Remove-Item -LiteralPath $ccFile}}
Write-Output 'PASS updater version, release boundary and install guard'
if($env:COMMANDCODE_TEST_UPDATER_LIVE -eq '1') {
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $ccUpdater.Check('1.0.0').GetAwaiter().GetResult()
    if($ccUpdater.Status -ne 'available'){throw $ccUpdater.Error}
    if(-not $ccUpdater.Notes -or -not $ccUpdater.PublishedAt){throw 'Release notes missing'}
    $ccUpdater.Download().GetAwaiter().GetResult()
    if($ccUpdater.Status -ne 'ready'){throw $ccUpdater.Error}
    Write-Output "PASS live GitHub release metadata, download and SHA256 v$($ccUpdater.Latest)"
}
