$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ccHost=Join-Path $ccRoot 'desktop/app/Host.dll'
[Reflection.Assembly]::LoadFrom($ccHost)|Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'LocalProxyChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll'
[LocalProxyChecks]::Run()
Add-Type -Path (Join-Path $PSScriptRoot 'OpenCodeProxyChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll'
[OpenCodeProxyChecks]::Run()
Add-Type -Path (Join-Path $PSScriptRoot 'ProxyQuotaChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll'
[ProxyQuotaChecks]::Run()
Add-Type -Path (Join-Path $PSScriptRoot 'ProxyNetworkChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll'
[ProxyNetworkChecks]::Run()
Add-Type -Path (Join-Path $PSScriptRoot 'ProxyWorkspaceChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll'
[ProxyWorkspaceChecks]::Run()
Add-Type -Path (Join-Path $PSScriptRoot 'ProxyLogChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Web.Extensions.dll'
$ccLogTemp=Join-Path ([IO.Path]::GetTempPath()) ('cmd-proxy-log-test-'+[guid]::NewGuid().ToString('N'))
[ProxyLogChecks]::Run($ccLogTemp)
