$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ccHost=Join-Path $ccRoot 'desktop/app/Host.dll'
[Reflection.Assembly]::LoadFrom($ccHost)|Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'PortableFeatureChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll','System.Web.Extensions.dll','System.Security.dll'
$ccTemp=Join-Path ([IO.Path]::GetTempPath()) ('cmd-portable-test-'+[guid]::NewGuid().ToString('N'))
[PortableFeatureChecks]::Run($ccTemp)