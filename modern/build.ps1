$ErrorActionPreference = 'Stop'
$ccRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ccDesktop = Join-Path (Split-Path -Parent $ccRoot) 'desktop'
$ccApp = Join-Path $ccDesktop 'app'
$ccStage = Join-Path $ccRoot 'stage'
New-Item -ItemType Directory -Force -Path $ccDesktop,$ccApp,$ccStage | Out-Null
$ccCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ccNative = Join-Path (Split-Path -Parent $ccRoot) 'native'
$ccSdk = Join-Path $ccRoot 'sdk\lib\net462'
if (-not (Test-Path -LiteralPath "$ccDesktop\Contracts.dll")) { & $ccCompiler /nologo /target:library "/out:$ccDesktop\Contracts.dll" "$ccRoot\Contracts.cs" }
Copy-Item -LiteralPath "$ccDesktop\Contracts.dll" -Destination $ccApp -Force
$ccRefs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Net.Http.dll','/reference:System.Security.dll',"/reference:$ccDesktop\Contracts.dll","/reference:$ccSdk\Microsoft.Web.WebView2.Core.dll","/reference:$ccSdk\Microsoft.Web.WebView2.WinForms.dll")
& $ccCompiler /nologo /target:library /optimize+ /platform:x64 /utf8output "/resource:$ccRoot\assets\manager.ico,CommandCodeManager.Icon" "/resource:$(Join-Path (Split-Path -Parent $ccRoot) 'version.json'),CommandCodeManager.Version" "/resource:$(Join-Path (Split-Path -Parent $ccRoot) 'docs\proxy-usage.openapi.json'),CommandCodeManager.ProxyUsageOpenApi" @ccRefs "/out:$ccStage\Host.dll" "$ccRoot\Host.cs" "$ccRoot\AppUpdater.cs" "$ccRoot\LocalProxy.cs" "$ccRoot\ProxyWorkspace.cs" "$ccRoot\ProxyClientSetup.cs" "$ccRoot\OpenCodeProxyModels.cs" "$ccRoot\OpenCode.cs" "$ccRoot\OfficialUsage.cs" "$ccRoot\TokenHistory.cs" "$ccRoot\TokenHistoryCache.cs" "$ccRoot\AccountTransfer.cs" "$ccRoot\UsageCsv.cs" "$ccNative\Manager.cs" "$ccNative\ModernControls.cs"
if ($LASTEXITCODE -ne 0) { throw '桌面服务编译失败' }
& $ccCompiler /nologo /target:winexe /optimize+ /platform:x64 /reference:System.Windows.Forms.dll "/reference:$ccDesktop\Contracts.dll" "/win32icon:$ccRoot\assets\manager.ico" "/out:$ccStage\CommandCode Manager.exe" "$ccRoot\Launcher.cs"
if ($LASTEXITCODE -ne 0) { throw '固定入口编译失败' }
if (-not (Test-Path -LiteralPath "$ccDesktop\CommandCode Manager.exe")) { Copy-Item -LiteralPath "$ccStage\CommandCode Manager.exe" -Destination "$ccDesktop\CommandCode Manager.exe" }
Copy-Item -LiteralPath "$ccStage\Host.dll" -Destination "$ccApp\Host.dll" -Force
New-Item -ItemType Directory -Force -Path "$ccApp\addons" | Out-Null
Get-ChildItem -LiteralPath "$ccRoot\addons" -File | Copy-Item -Destination "$ccApp\addons" -Force
Copy-Item -LiteralPath "$ccSdk\Microsoft.Web.WebView2.Core.dll","$ccSdk\Microsoft.Web.WebView2.WinForms.dll" -Destination $ccApp -Force
if (-not (Test-Path -LiteralPath "$ccDesktop\WebView2Loader.dll")) { Copy-Item -LiteralPath "$ccRoot\sdk\runtimes\win-x64\native\WebView2Loader.dll" -Destination "$ccDesktop\WebView2Loader.dll" }
$ccUi = Join-Path $ccApp 'ui'
New-Item -ItemType Directory -Force -Path $ccUi,(Join-Path $ccUi 'assets') | Out-Null
Get-ChildItem -LiteralPath "$ccRoot\ui\dist\assets" -File | Copy-Item -Destination (Join-Path $ccUi 'assets') -Force
$ccBuildTag = [DateTime]::UtcNow.Ticks.ToString()
$ccHtml = Get-Content -LiteralPath "$ccRoot\ui\dist\index.html" -Raw
$ccHtml = $ccHtml.Replace('assets/app.js',"assets/app.js?v=$ccBuildTag").Replace('assets/app.css',"assets/app.css?v=$ccBuildTag")
[IO.File]::WriteAllText((Join-Path $ccUi 'index.html'),$ccHtml,[Text.UTF8Encoding]::new($false))
[DateTime]::UtcNow.ToString('o') | Set-Content -LiteralPath (Join-Path $ccUi 'version.txt') -Encoding utf8
Get-Item -LiteralPath "$ccDesktop\CommandCode Manager.exe","$ccApp\Host.dll" | Select-Object FullName,Length
