$ErrorActionPreference = 'Stop'
$ccRoot = Split-Path -Parent $PSScriptRoot
Push-Location $ccRoot
try {
    $ccFiles = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0 -or $ccFiles.Count -eq 0) { throw '没有可检查的源文件' }
    $ccForbidden = @($ccFiles | Where-Object { $_ -match '(^|/)(desktop|node_modules|sdk|stage[^/]*|downloads|output|release[^/]*|preview[^/]*)/|(^|/)(auth|accounts[^/]*|usage-history)\.json$|\.(ccaccounts|encrypted|exe|dll|zip|log)$' })
    if ($ccForbidden.Count -gt 0) { throw ('发现不应上传的文件：' + ($ccForbidden -join ', ')) }
    $ccPrivateValues = New-Object 'System.Collections.Generic.List[string]'
    $ccLocalStore = Join-Path $env:APPDATA 'commandcode-account-manager\accounts-native.json'
    if (Test-Path -LiteralPath $ccLocalStore) {
        Add-Type -AssemblyName System.Security
        try { $ccStore = Get-Content -LiteralPath $ccLocalStore -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop } catch { throw '本地账号文件无法读取，停止发布检查，原文件保留' }
        if ($ccStore.Proxy.Secret) {
            $ccPrivateValues.Add([string]$ccStore.Proxy.Secret)
            $ccProxyKeyBytes=[Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($ccStore.Proxy.Secret),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
            $ccPrivateValues.Add([Text.Encoding]::UTF8.GetString($ccProxyKeyBytes))
        }
        foreach ($ccAccount in $ccStore.Accounts) {
            $ccIdentityOrg = $ccAccount.Data.OrgId
            if ($ccAccount.Platform -eq 'opencode' -and $ccIdentityOrg -eq $ccAccount.ProviderId) { $ccIdentityOrg = $null }
            $ccIdentityEmail = $ccAccount.Data.Email
            if ($ccAccount.Platform -eq 'opencode' -and $ccIdentityEmail -notmatch '@') { $ccIdentityEmail = $null }
            foreach ($ccValue in @($ccAccount.Id,$ccAccount.Secret,$ccAccount.Data.UserId,$ccIdentityOrg,$ccIdentityEmail)) {
                if ($ccValue -and ([string]$ccValue).Length -ge 6) { $ccPrivateValues.Add([string]$ccValue) }
            }
            if ($ccAccount.Secret) {
                $ccBytes = [Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($ccAccount.Secret),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
                $ccPrivateValues.Add([Text.Encoding]::UTF8.GetString($ccBytes))
            }
        }
    }
    $ccMatches = New-Object 'System.Collections.Generic.List[string]'
    $ccOpenCodeAuth = Join-Path $env:USERPROFILE '.local\share\opencode\auth.json'
    if (Test-Path -LiteralPath $ccOpenCodeAuth) {
        try { $ccOpenCodeValues = Get-Content -LiteralPath $ccOpenCodeAuth -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop } catch { throw 'OpenCode 登录文件无法读取，停止发布检查，原文件保留' }
        foreach ($ccProvider in $ccOpenCodeValues.psobject.Properties) {
            foreach ($ccField in @('key','access','refresh')) {
                $ccValue = $ccProvider.Value.$ccField
                if ($ccValue -and ([string]$ccValue).Length -ge 16) { $ccPrivateValues.Add([string]$ccValue) }
            }
        }
    }
    foreach ($ccFile in $ccFiles) {
        $ccContent = [IO.File]::ReadAllText((Join-Path $ccRoot $ccFile))
        foreach ($ccValue in $ccPrivateValues) {
            if ($ccContent.Contains($ccValue)) { $ccMatches.Add($ccFile); break }
        }
        if ($ccContent -match '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)') { $ccMatches.Add($ccFile) }
    }
    if ($ccMatches.Count -gt 0) { throw ('源码发现敏感值，停止发布；涉及文件：' + (($ccMatches | Select-Object -Unique) -join ', ')) }
    Write-Output "PASS: $($ccFiles.Count) source files checked; local account identities and credentials excluded"
} finally { Pop-Location }
