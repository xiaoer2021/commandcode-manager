$ErrorActionPreference = 'Stop'
$ccNativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ccNativeOutput = Join-Path (Split-Path -Parent $ccNativeRoot) 'release-native-v3'
New-Item -ItemType Directory -Force -Path $ccNativeOutput | Out-Null
$ccCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $ccCompiler /nologo /target:winexe /optimize+ /platform:anycpu /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll /reference:System.Security.dll /out:"$ccNativeOutput\CommandCode Manager.exe" "$ccNativeRoot\Manager.cs" "$ccNativeRoot\ModernControls.cs"
if ($LASTEXITCODE -ne 0) { throw '编译未通过' }
Get-Item -LiteralPath "$ccNativeOutput\CommandCode Manager.exe" | Select-Object FullName, Length
