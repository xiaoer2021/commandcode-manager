$ErrorActionPreference='Stop'
$ccRoot=Split-Path -Parent $PSScriptRoot
$ccVersion=(Get-Content (Join-Path $ccRoot 'version.json') -Raw -Encoding UTF8|ConvertFrom-Json).version
$ccImages=Join-Path $ccRoot "docs/screenshots/v$ccVersion"
$ccManifestPath=Join-Path $ccImages 'manifest.json'
if(-not(Test-Path -LiteralPath $ccManifestPath)){throw 'Current release screenshot manifest is missing'}
$ccManifest=Get-Content -LiteralPath $ccManifestPath -Raw -Encoding UTF8|ConvertFrom-Json
if($ccManifest.version -ne $ccVersion -or $ccManifest.demoData -ne $true -or @($ccManifest.images).Count -lt 1){throw 'Screenshot manifest must match the release and use demonstration data'}
$ccNotes=Get-Content (Join-Path $ccRoot 'RELEASE_NOTES.md') -Raw -Encoding UTF8
$ccSignature=[byte[]](137,80,78,71,13,10,26,10)
$ccLinked=$false
$ccSeen=@{}
foreach($ccImage in $ccManifest.images){
    if($ccImage.file -notmatch '^[a-z0-9][a-z0-9-]*\.png$' -or [string]::IsNullOrWhiteSpace($ccImage.title) -or $ccSeen.ContainsKey($ccImage.file)){throw 'Invalid or duplicate screenshot entry'}
    $ccSeen[$ccImage.file]=$true
    $ccImagePath=Join-Path $ccImages $ccImage.file
    if(-not(Test-Path -LiteralPath $ccImagePath)){throw 'A declared release screenshot is missing'}
    $ccBytes=[IO.File]::ReadAllBytes($ccImagePath)
    if($ccBytes.Length -lt 24 -or $ccBytes.Length -gt 5242880){throw 'Screenshot size is invalid'}
    for($ccIndex=0;$ccIndex -lt 8;$ccIndex++){if($ccBytes[$ccIndex] -ne $ccSignature[$ccIndex]){throw 'Release screenshot must be a PNG'}}
    $ccLink="https://github.com/xiaoer2021/commandcode-manager/releases/download/v$ccVersion/$($ccImage.file)"
    if($ccNotes.Contains($ccLink)){$ccLinked=$true}
}
if(-not $ccLinked){throw 'Release notes must embed at least one current release screenshot URL'}
Write-Output "PASS: $(@($ccManifest.images).Count) release screenshots declared for v$ccVersion; demonstration data confirmed in manifest"
