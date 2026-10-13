$ErrorActionPreference = 'Stop'
$ccModernRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& npm --prefix (Join-Path $ccModernRoot 'ui') run build
if ($LASTEXITCODE -ne 0) { throw '界面构建未通过' }
& (Join-Path $ccModernRoot 'build.ps1')
$ccClientPipe = [IO.Pipes.NamedPipeClientStream]::new('.','CommandCodeManagerControl',[IO.Pipes.PipeDirection]::Out)
try {
$ccClientPipe.Connect(500)
$ccClientWriter = [IO.StreamWriter]::new($ccClientPipe)
$ccClientWriter.WriteLine('update')
$ccClientWriter.Flush()
$ccClientWriter.Dispose()
Write-Output '已通知固定程序加载更新，账号数据保留。'
} catch [TimeoutException] { Write-Output '更新已就位，下次从固定入口打开即生效。' }
finally { $ccClientPipe.Dispose() }
