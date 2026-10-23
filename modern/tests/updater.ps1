$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
[Reflection.Assembly]::LoadFrom((Join-Path $ccRoot 'desktop/app/Host.dll'))|Out-Null
if(-not [ModernHost.AppUpdater]::Newer('1.1.0','1.0.1')){throw 'New release not detected'}
if([ModernHost.AppUpdater]::Newer('1.0.1','1.1.0')){throw 'Downgrade offered'}
if([ModernHost.AppUpdater]::Newer('bad','1.0.1')){throw 'Invalid version accepted'}
if([ModernHost.AppUpdater]::Newer('1.1.0','1.1.0')){throw 'Same version offered'}
$ccUrl='https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.1.0/CommandCode-Manager-Setup-1.1.0.exe'
if(-not [ModernHost.AppUpdater]::Trusted($ccUrl,'1.1.0','CommandCode-Manager-Setup-1.1.0.exe')){throw 'Own release rejected'}
if([ModernHost.AppUpdater]::LatestTagFromUrl('https://github.com/xiaoer2021/commandcode-manager/releases/tag/v1.5.3') -ne 'v1.5.3'){throw 'Latest stable tag parsing failed'}
$ccWrongReleaseRejected=$false
try{[void][ModernHost.AppUpdater]::LatestTagFromUrl('https://github.com/xiaoer2021/other/releases/tag/v1.5.3')}catch{$ccWrongReleaseRejected=$true}
if(-not $ccWrongReleaseRejected){throw 'Wrong repository latest URL accepted'}
$ccNotes='<feed xmlns="http://www.w3.org/2005/Atom"><entry><title>Workspace v1.5.3</title><updated>2026-10-23T08:30:37Z</updated><content type="html">&amp;lt;h2&amp;gt;本次更新&amp;lt;/h2&amp;gt;&amp;lt;ul&amp;gt;&amp;lt;li&amp;gt;支持更新&amp;lt;/li&amp;gt;&amp;lt;/ul&amp;gt;</content></entry></feed>'
$ccPublished='';$ccParsedNotes=[ModernHost.AppUpdater]::NotesFromAtom($ccNotes,'v1.5.3',[ref]$ccPublished)
if($ccParsedNotes -notmatch '支持更新' -or $ccParsedNotes -notmatch '- 支持更新' -or $ccPublished -ne '2026-10-23T08:30:37Z'){throw 'Atom update notes parsing failed'}
if([ModernHost.AppUpdater]::Trusted($ccUrl.Replace('xiaoer2021','attacker'),'1.1.0','CommandCode-Manager-Setup-1.1.0.exe')){throw 'Wrong repository accepted'}
if([ModernHost.AppUpdater]::Trusted($ccUrl.Replace('https:','http:'),'1.1.0','CommandCode-Manager-Setup-1.1.0.exe')){throw 'HTTP accepted'}
$ccUpdater=New-Object ModernHost.AppUpdater
if(-not $ccUpdater.AutoCheckDue([DateTimeOffset]::UtcNow)){throw 'Startup auto check missing'}
$ccUpdater.LastAttempt=[DateTimeOffset]::UtcNow.AddMinutes(-6)
if($ccUpdater.AutoCheckDue([DateTimeOffset]::UtcNow)){throw 'Successful auto check repeated too often'}
$ccUpdater.Status='error'
if(-not $ccUpdater.AutoCheckDue([DateTimeOffset]::UtcNow)){throw 'Failed automatic check not retried'}
$ccUpdater.Status='ready'
if($ccUpdater.AutoCheckDue([DateTimeOffset]::UtcNow.AddHours(2))){throw 'Downloaded installer discarded by automatic check'}
$ccUpdater.Status='idle'
$ccRejected=$false
try{$ccUpdater.Install()}catch{$ccRejected=$true}
if(-not $ccRejected){throw 'Unverified installation accepted'}
$ccFile=Join-Path ([IO.Path]::GetTempPath()) ('cc-update-check-'+[guid]::NewGuid().ToString('N'))
try {
    [IO.File]::WriteAllText($ccFile,'installer fixture')
    $ccSha=[Security.Cryptography.SHA256]::Create()
    try {$ccHash=[BitConverter]::ToString($ccSha.ComputeHash([IO.File]::ReadAllBytes($ccFile))).Replace('-','')} finally {$ccSha.Dispose()}
    $setup=$ccFile; $expected=$ccHash.ToLowerInvariant()
    function Get-FileHash {throw 'Get-FileHash must not be used'}
    Invoke-Expression ([ModernHost.AppUpdater]::HashCheckScript())
    if(-not [ModernHost.AppUpdater]::VerifyFile($ccFile,$ccHash)){throw 'Valid checksum rejected'}
    [IO.File]::AppendAllText($ccFile,'modified')
    if([ModernHost.AppUpdater]::VerifyFile($ccFile,$ccHash)){throw 'Tampered file accepted'}
    $ccTamperedRejected=$false
    try {Invoke-Expression ([ModernHost.AppUpdater]::HashCheckScript())} catch {$ccTamperedRejected=$true}
    if(-not $ccTamperedRejected){throw 'Helper accepted tampered installer'}
    $ccLock=[IO.File]::Open($ccFile,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $ccLock.Dispose()
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
