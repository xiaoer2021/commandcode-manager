$ErrorActionPreference = 'Stop'
$ccSdk = Join-Path $PSScriptRoot 'sdk'
if (Test-Path -LiteralPath (Join-Path $ccSdk 'lib\net462\Microsoft.Web.WebView2.Core.dll')) { return }
$ccPackage = Join-Path $PSScriptRoot 'webview2.zip'
Invoke-WebRequest -UseBasicParsing 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3537.50/microsoft.web.webview2.1.0.3537.50.nupkg' -OutFile $ccPackage
Expand-Archive -LiteralPath $ccPackage -DestinationPath $ccSdk -Force
