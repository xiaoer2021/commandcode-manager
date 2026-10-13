Add-Type -AssemblyName System.Drawing
$ccRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$ccExe=Join-Path $ccRoot 'modern/stage/CommandCode Manager.exe'
$ccIcon=[Drawing.Icon]::ExtractAssociatedIcon($ccExe)
try{$ccBitmap=$ccIcon.ToBitmap();try{$ccBitmap.Save((Join-Path $PSScriptRoot 'packaged-icon.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$ccBitmap.Dispose()};Write-Output ('PackagedIcon='+$ccIcon.Width+'x'+$ccIcon.Height)}finally{$ccIcon.Dispose()}
$ccAssembly=[Reflection.Assembly]::LoadFrom((Join-Path $ccRoot 'desktop/app/Host.dll'))
$ccStream=$ccAssembly.GetManifestResourceStream('CommandCodeManager.Icon')
try{Write-Output ('EmbeddedIconBytes='+$ccStream.Length)}finally{$ccStream.Dispose()}