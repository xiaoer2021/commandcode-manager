$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tests = $PSScriptRoot
$ccRoot = Split-Path $tests -Parent
$projRoot = Split-Path $ccRoot -Parent
$hostDll = Join-Path $projRoot 'desktop\app\Host.dll'
$out = Join-Path $projRoot 'desktop\app\TestRunner.exe'
$src = @(
  (Join-Path $tests 'TestRunner.cs'),
  (Join-Path $tests 'HistoryCacheChecks.cs'),
  (Join-Path $tests 'LocalProxyChecks.cs'),
  (Join-Path $tests 'OpenCodeChecks.cs'),
  (Join-Path $tests 'OpenCodeProxyChecks.cs'),
  (Join-Path $tests 'PortableFeatureChecks.cs'),
  (Join-Path $tests 'ProxyLogChecks.cs'),
  (Join-Path $tests 'ProxyNetworkChecks.cs'),
  (Join-Path $tests 'ProxyQuotaChecks.cs'),
  (Join-Path $tests 'ProxyWorkspaceChecks.cs'),
  (Join-Path $ccRoot 'CoreChecks.cs')
)
& $csc /nologo /target:exe /platform:x64 /codepage:65001 /main:TestRunner /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll /reference:System.Security.dll "/reference:$hostDll" "/out:$out" $src
if ($LASTEXITCODE -ne 0) { throw 'test compile failed' }
Write-Output ("compiled OK: " + (Get-Item -LiteralPath $out).Length + " bytes")
