param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$ccRoot = Split-Path -Parent $PSScriptRoot
$ccConfig = Get-Content -LiteralPath (Join-Path $ccRoot 'version.json') -Raw | ConvertFrom-Json
if ($ccConfig.version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须是 x.y.z' }
if (-not $SkipBuild) {
    & (Join-Path $ccRoot 'modern\restore-sdk.ps1')
    & npm --prefix (Join-Path $ccRoot 'modern\ui') ci
    if ($LASTEXITCODE -ne 0) { throw '界面依赖安装失败' }
    & npm --prefix (Join-Path $ccRoot 'modern\ui') run build
    if ($LASTEXITCODE -ne 0) { throw '界面构建失败' }
    & (Join-Path $ccRoot 'modern\build.ps1')
}
$ccStage = Join-Path $PSScriptRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
$ccAllowlist = @('CommandCode Manager.exe','Contracts.dll','WebView2Loader.dll','app\Host.dll','app\Contracts.dll','app\Microsoft.Web.WebView2.Core.dll','app\Microsoft.Web.WebView2.WinForms.dll','app\ui\index.html','app\ui\version.txt','app\ui\assets\app.js','app\ui\assets\app.css','app\addons\manager-credits-loader.js','app\addons\manager-credits-panel.js')
foreach ($ccRelative in $ccAllowlist) {
    $ccDestination = Join-Path $ccStage $ccRelative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ccDestination)) | Out-Null
    Copy-Item -LiteralPath (Join-Path (Join-Path $ccRoot 'desktop') $ccRelative) -Destination $ccDestination
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.md') -Destination (Join-Path $ccStage '使用说明.md')
$ccDownloads = Join-Path $PSScriptRoot 'downloads'
[IO.Directory]::CreateDirectory($ccDownloads) | Out-Null
$ccRuntime = Join-Path $ccDownloads 'MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path -LiteralPath $ccRuntime)) { Invoke-WebRequest -UseBasicParsing 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $ccRuntime }
$ccSignature = Get-AuthenticodeSignature -LiteralPath $ccRuntime
if ($ccSignature.Status -ne 'Valid' -or $ccSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'WebView2 安装文件签名校验失败' }
$ccCompiler = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $ccCompiler) { $ccCompiler = (Get-Command ISCC.exe -ErrorAction Stop).Source }
& $ccCompiler "/DAppVersion=$($ccConfig.version)" "/DStageDir=$ccStage" "/DRuntimeInstaller=$ccRuntime" (Join-Path $PSScriptRoot 'manager.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败' }
$ccRelease = Join-Path $ccRoot 'release'
[IO.Directory]::CreateDirectory($ccRelease) | Out-Null
$ccInstallerName = "CommandCode-Manager-Setup-$($ccConfig.version).exe"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "output\$ccInstallerName") -Destination (Join-Path $ccRelease $ccInstallerName) -Force
Compress-Archive -Path (Join-Path $ccStage '*') -DestinationPath (Join-Path $ccRelease "CommandCode-Manager-Portable-$($ccConfig.version).zip") -Force
$ccArtifacts = Get-ChildItem -LiteralPath $ccRelease -File | Where-Object { $_.Name -eq $ccInstallerName -or $_.Name -eq "CommandCode-Manager-Portable-$($ccConfig.version).zip" }
$ccArtifacts | ForEach-Object { "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" } | Set-Content -LiteralPath (Join-Path $ccRelease 'SHA256SUMS.txt') -Encoding ascii
$ccArtifacts | Select-Object Name,Length
