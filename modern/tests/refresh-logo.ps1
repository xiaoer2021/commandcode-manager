$ErrorActionPreference='Stop'
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class LogoShell{[DllImport("shell32.dll",CharSet=CharSet.Unicode)]public static extern void SHChangeNotify(uint change,uint flags,string path,IntPtr item2);}'
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
[LogoShell]::SHChangeNotify(0x2000,0x1005,(Join-Path $ccRoot 'desktop/CommandCode Manager.exe'),[IntPtr]::Zero)