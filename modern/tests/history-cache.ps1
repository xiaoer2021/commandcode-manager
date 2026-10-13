$ErrorActionPreference='Stop'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ccHost=Join-Path $ccRoot 'desktop/app/Host.dll'
[Reflection.Assembly]::LoadFrom($ccHost)|Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'HistoryCacheChecks.cs') -ReferencedAssemblies $ccHost,'System.Core.dll'
$ccTemp=Join-Path ([IO.Path]::GetTempPath()) ('cmd-history-cache-test-'+[guid]::NewGuid().ToString('N'))
[HistoryCacheChecks]::Run($ccTemp)