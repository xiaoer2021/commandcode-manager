$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$ccRoot=Split-Path -Parent $PSScriptRoot
$ccSource=[Drawing.Image]::FromFile('C:\Users\Administrator\Pictures\IMG_0987.jpg')
$ccFrames=@()
try {
foreach($ccSize in @(16,24,32,48,64,128,256)) {
$ccBitmap=[Drawing.Bitmap]::new($ccSize,$ccSize)
$ccGraphics=[Drawing.Graphics]::FromImage($ccBitmap)
try {
$ccGraphics.Clear([Drawing.Color]::White)
$ccGraphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$ccGraphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$ccScale=[Math]::Min($ccSize/$ccSource.Width,$ccSize/$ccSource.Height)
$ccWidth=[int][Math]::Round($ccSource.Width*$ccScale)
$ccHeight=[int][Math]::Round($ccSource.Height*$ccScale)
$ccGraphics.DrawImage($ccSource,[int](($ccSize-$ccWidth)/2),[int](($ccSize-$ccHeight)/2),$ccWidth,$ccHeight)
$ccMemory=[IO.MemoryStream]::new()
try {$ccBitmap.Save($ccMemory,[Drawing.Imaging.ImageFormat]::Png);$ccFrames+=,@{Size=$ccSize;Data=$ccMemory.ToArray()}}finally{$ccMemory.Dispose()}
if($ccSize -eq 128){$ccBitmap.Save((Join-Path $ccRoot 'assets/manager-logo.png'),[Drawing.Imaging.ImageFormat]::Png)}
}finally{$ccGraphics.Dispose();$ccBitmap.Dispose()}
}
$ccOutput=[IO.File]::Create((Join-Path $ccRoot 'assets/manager.ico'))
$ccWriter=[IO.BinaryWriter]::new($ccOutput)
try {
$ccWriter.Write([uint16]0);$ccWriter.Write([uint16]1);$ccWriter.Write([uint16]$ccFrames.Count)
$ccOffset=6+16*$ccFrames.Count
foreach($ccFrame in $ccFrames){$ccDimension=if($ccFrame.Size -eq 256){0}else{$ccFrame.Size};$ccWriter.Write([byte]$ccDimension);$ccWriter.Write([byte]$ccDimension);$ccWriter.Write([byte]0);$ccWriter.Write([byte]0);$ccWriter.Write([uint16]1);$ccWriter.Write([uint16]32);$ccWriter.Write([uint32]$ccFrame.Data.Length);$ccWriter.Write([uint32]$ccOffset);$ccOffset+=$ccFrame.Data.Length}
foreach($ccFrame in $ccFrames){$ccWriter.Write([byte[]]$ccFrame.Data)}
}finally{$ccWriter.Dispose();$ccOutput.Dispose()}
}finally{$ccSource.Dispose()}
Write-Output 'Logo and multi-size application icon created'