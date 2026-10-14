#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef StageDir
  #error StageDir is required
#endif
#ifndef RuntimeInstaller
  #error RuntimeInstaller is required
#endif
[Setup]
AppId={{B39AE438-8327-4407-B38E-620E8109AA67}
AppName=多账号工作台
AppVersion={#AppVersion}
AppPublisher=多账号工作台
DefaultDirName={localappdata}\Programs\CommandCode Manager
DefaultGroupName=多账号工作台
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename=CommandCode-Manager-Setup-{#AppVersion}
SetupIconFile=..\modern\assets\manager.ico
UninstallDisplayIcon={app}\CommandCode Manager.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
[Tasks]
Name: desktopicon; Description: "创建桌面快捷方式"; Flags: checkedonce
[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2
[Icons]
Name: "{group}\多账号工作台"; Filename: "{app}\CommandCode Manager.exe"
Name: "{autodesktop}\多账号工作台"; Filename: "{app}\CommandCode Manager.exe"; Tasks: desktopicon
[InstallDelete]
Type: files; Name: "{autodesktop}\CommandCode 管理器.lnk"
Type: files; Name: "{userprograms}\CommandCode 管理器\CommandCode 管理器.lnk"
[Run]
Filename: "{app}\CommandCode Manager.exe"; Description: "打开 多账号工作台"; Flags: nowait postinstall skipifsilent
[Code]
function NeedsWebView2: Boolean;
var InstalledVersion: String;
begin
  Result := not (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', InstalledVersion) and (InstalledVersion <> '') and (InstalledVersion <> '0.0.0.0'));
  if Result then Result := not (RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', InstalledVersion) and (InstalledVersion <> '') and (InstalledVersion <> '0.0.0.0'));
  if Result then Result := not (RegQueryStringValue(HKLM64, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', InstalledVersion) and (InstalledVersion <> '') and (InstalledVersion <> '0.0.0.0'));
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Local\CommandCodeModernHost') then
    Result := '管理器仍在后台运行。请在右下角托盘右键选择“彻底退出”，然后点击重试。';
end;
procedure CurStepChanged(CurStep: TSetupStep);
var ExitCode: Integer;
begin
  if (CurStep = ssPostInstall) and NeedsWebView2 then begin
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('无法安装 WebView2，请检查网络后重新运行安装包。');
    if NeedsWebView2 then
      RaiseException('WebView2 安装未完成。请从 Microsoft 官方网站安装 WebView2 Runtime 后再打开程序。');
  end;
end;
function InitializeUninstall: Boolean;
begin
  Result := not CheckForMutexes('Local\CommandCodeModernHost');
  if not Result then MsgBox('请先在托盘中彻底退出管理器，再卸载。账号和用量数据会保留。', mbInformation, MB_OK);
end;
