using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CommandCodeManager;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace ModernHost {
public class DomainRunner : MarshalByRefObject, FixedContract.IRunner {
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public override object InitializeLifetimeService() { return null; }
    public bool Run(string[] args) {
        try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; ServicePointManager.DefaultConnectionLimit = 8; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool hot = args.Contains("--verify-hot"), test = args.Contains("--verify") || hot, live = args.Contains("--verify-live"); bool owner;
        using (Mutex mutex = new Mutex(true, test || live ? "Local\\CommandCodeModernVerify" : "Local\\CommandCodeModernHost", out owner)) {
            if (!owner) { try { using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", "CommandCodeManagerControl", PipeDirection.Out)) { pipe.Connect(1000); using (StreamWriter writer = new StreamWriter(pipe)) { writer.WriteLine("show"); writer.Flush(); } } } catch { } return false; }
            using (DesktopForm form = new DesktopForm(test, live, hot)) { Application.Run(form); if(hot)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","control-trace.txt"),"loop ended: "+form.Restart+Environment.NewLine); return form.Restart; }
        }
    }
}
public class DesktopForm : Form {
    private sealed class AccountAlreadyExistsException : InvalidOperationException { public AccountAlreadyExistsException() : base("这个账号已添加，无需重复导入") {} }
    private readonly string appDir = Path.GetDirectoryName(typeof(DesktopForm).Assembly.Location);
    private readonly string deployedDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
    private readonly string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "commandcode-account-manager", "accounts-native.json");
    private readonly string authFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".commandcode", "auth.json");
    private readonly Dictionary<string, string> keys = new Dictionary<string, string>();
    private readonly object gate = new object();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer(), reloadTimer = new System.Windows.Forms.Timer();
    private WebView2 view;
    private CoreWebView2Environment environment;
    private FileSystemWatcher watcher;
    private FileSystemWatcher authWatcher;
    private Store store;
    private bool busy, quitting, initializing, ready, verifying, liveVerify, hotVerify;
    private int navigations;
    private int completed;
    private string activeId;
    private LoginFlow login;
    private readonly HashSet<string> refreshingAccounts=new HashSet<string>();
    private string importFile;
    private bool transferBusy;
    private bool addingAccount;
    private bool resizePrompt;
    private Size defaultClientSize,resizeStartSize;
    private LocalProxy localProxy;
    private readonly AppUpdater updater=new AppUpdater();
    private readonly System.Windows.Forms.Timer updateTimer=new System.Windows.Forms.Timer(),renewalTimer=new System.Windows.Forms.Timer();
    private readonly string appVersion=ReadAppVersion();
    private static string ReadAppVersion() { using(Stream resource=typeof(DesktopForm).Assembly.GetManifestResourceStream("CommandCodeManager.Version")) { if(resource==null)return "本地开发";using(StreamReader reader=new StreamReader(resource)){return Core.Text(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd()),"version");} } }
    private bool refreshPaused = false;
    private string officialSignature;
    private readonly TokenHistoryCache historyCache;
    private readonly OpenCodeService openCode=new OpenCodeService();
    private readonly HashSet<string> openCodeActive=new HashSet<string>();
    private string openCodeAttempt,openCodeAttemptProvider;
    private readonly Dictionary<string,List<HistoryQuery>> pendingHistory=new Dictionary<string,List<HistoryQuery>>();
    private readonly HashSet<string> queuedHistory=new HashSet<string>(),runningHistory=new HashSet<string>();
    private readonly HashSet<string> switching = new HashSet<string>();
    public bool Restart;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetDllDirectory(string path);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public DesktopForm(bool test, bool live, bool hot) {
        verifying = test; liveVerify = live; hotVerify = hot;
        SetDllDirectory(AppDomain.CurrentDomain.BaseDirectory);
        Text = "多账号工作台"; ClientSize = new Size(1240, 888); MinimumSize = new Size(1100, 680); StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(220, 230, 243); Font = new Font("Microsoft YaHei UI", 9); AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None; Padding = new Padding(0); MaximizedBounds = Screen.FromControl(this).WorkingArea;
        double dpiScale = GetDpiForWindow(Handle) / 96.0; Rectangle desktopArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min((int)(1100 * dpiScale), desktopArea.Width - 32), Math.Min((int)(680 * dpiScale), desktopArea.Height - 40));
        ClientSize = new Size(Math.Min((int)(1240 * dpiScale), desktopArea.Width - 40), Math.Min((int)(888 * dpiScale), desktopArea.Height - 40));
        defaultClientSize=ClientSize;
        try { int corner = 2; DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int)); int borderColor = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(Handle, 34, ref borderColor, sizeof(int)); } catch (DllNotFoundException) { }
        if (test) { file = Path.Combine(Path.GetTempPath(), "commandcode-react-test-" + Guid.NewGuid().ToString("N"), "accounts.json"); store = new Store(); Seed(); }
        else { store = File.Exists(file) ? new JavaScriptSerializer {MaxJsonLength=8388608}.Deserialize<Store>(File.ReadAllText(file)) : new Store(); if (store == null || store.Accounts == null || store.Settings == null) throw new InvalidOperationException("账号数据格式异常，原文件已保留"); }
        if(!test&&!live&&store.Settings.WindowHeight>0){ClientSize=new Size(Math.Max(MinimumSize.Width,Math.Min((int)(store.Settings.WindowWidth*dpiScale),desktopArea.Width-40)),Math.Max(MinimumSize.Height,Math.Min((int)(store.Settings.WindowHeight*dpiScale),desktopArea.Height-40)));}
        historyCache=new TokenHistoryCache(Path.Combine(Path.GetDirectoryName(file),"usage-history.json"));
        foreach (Account account in store.Accounts) { try { keys[account.Id] = Core.Decrypt(account.Secret); } catch { account.Error = "凭据无法解密，请用原 Windows 用户或重新登录"; } }
        if(store.Proxy==null)store.Proxy=new ProxySettings();if(store.Proxy.AccountIds==null)store.Proxy.AccountIds=new List<string>();
        localProxy=new LocalProxy(ProxyAccounts,verifying?new ProxyVerificationHandler():null);localProxy.Changed=()=>{if(!IsDisposed&&!quitting)try{BeginInvoke(new Action(Push));}catch(InvalidOperationException){}};
        try{localProxy.Configure(store.Proxy.Port,string.IsNullOrEmpty(store.Proxy.Secret)?null:Core.Decrypt(store.Proxy.Secret),store.Proxy.AccountIds,!test&&!live&&store.Proxy.Enabled,store.Proxy.BindAddress??"127.0.0.1");}catch(Exception){localProxy.Error="本地代理未启动，请检查端口和代理 Key 后重新开启";}
        if (!test && !live && store.Settings.OfficialUsagePanel) { IdentifyActive(); Account migrated = store.Accounts.FirstOrDefault(a => a.Id == activeId); if (migrated != null) migrated.OfficialUsagePanel = true; store.Settings.OfficialUsagePanel = false; Save(); }
        if (!test) IdentifyActive();
        if (!test && !live && (store.Accounts.Any(a=>a.OfficialUsagePanel&&!OpenCodeService.IsOpenCode(a)) || OfficialUsage.IsInstalled())) { try { InstallOfficial(); } catch { } }
        using(Stream logo=typeof(DesktopForm).Assembly.GetManifestResourceStream("CommandCodeManager.Icon")) { if(logo!=null) using(Icon embedded=new Icon(logo,new Size(64,64))) Icon=(Icon)embedded.Clone(); }
        tray.Icon = Icon ?? SystemIcons.Application; tray.Text = "多账号工作台"; tray.Visible = !test && !live;
        ContextMenuStrip menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9), ShowImageMargin = false }; menu.Opening += (s,e) => BuildTrayMenu(menu); tray.ContextMenuStrip = menu; BuildTrayMenu(menu); tray.DoubleClick += async (s, e) => await ShowWindow(); tray.BalloonTipClicked += async (s, e) => await ShowWindow();
        refreshTimer.Tick += async (s, e) => await RefreshAll(); Schedule();
        reloadTimer.Interval = 500; reloadTimer.Tick += (s, e) => { if (busy || login != null || switching.Count > 0) return; reloadTimer.Stop(); if (ready && view != null) view.CoreWebView2.Reload(); };
        if (Directory.Exists(deployedDir)) { watcher = new FileSystemWatcher(deployedDir, "version.txt"); watcher.Changed += (s, e) => BeginInvoke(new Action(() => { reloadTimer.Stop(); reloadTimer.Start(); })); watcher.EnableRaisingEvents = true; }
        if (!test && !live && Directory.Exists(Path.GetDirectoryName(authFile))) {
            authWatcher = new FileSystemWatcher(Path.GetDirectoryName(authFile), "auth.json");
            FileSystemEventHandler authChanged = (s,e) => { if (quitting || IsDisposed) return; try { BeginInvoke(new Action(() => { if (!quitting) { IdentifyActive(); Push(); } })); } catch (InvalidOperationException) { } };
            authWatcher.Changed += authChanged; authWatcher.Created += authChanged; authWatcher.Deleted += authChanged; authWatcher.Renamed += (s,e) => authChanged(s,e); authWatcher.EnableRaisingEvents = true;
        }
        updater.Changed=()=>{if(!IsDisposed&&!quitting)try{BeginInvoke(new Action(Push));}catch(InvalidOperationException){}};
        renewalTimer.Interval=60000;renewalTimer.Tick+=(s,e)=>{bool changed=false;foreach(Account a in store.Accounts.Where(x=>!string.IsNullOrEmpty(x.RenewalAt)).ToArray()){string before=string.Join("|",a.AlertIds??new List<string>());Notify(a);changed|=before!=string.Join("|",a.AlertIds);}if(changed){Save();Push();}};
        updateTimer.Interval=60000;updateTimer.Tick+=async(s,e)=>await updater.CheckAutomatically(appVersion);
        Shown += async (s, e) => { await InitView(); if (!test&&!live){updateTimer.Start();renewalTimer.Start();Task ignored=updater.CheckAutomatically(appVersion);} if (!test) await RefreshAll(); };
        Activated += (s, e) => { if (!verifying) IdentifyActive(); Push(); };
        bool wasMaximized=false;Resize+=(s,e)=>{bool maximized=WindowState==FormWindowState.Maximized;if(maximized!=wasMaximized){wasMaximized=maximized;Push();}};
        ResizeBegin+=(s,e)=>{resizeStartSize=ClientSize;};
        ResizeEnd+=(s,e)=>{if(ready&&WindowState==FormWindowState.Normal&&ClientSize!=resizeStartSize){resizePrompt=true;if(!verifying&&!liveVerify){double scale=GetDpiForWindow(Handle)/96.0;store.Settings.WindowWidth=(int)Math.Round(ClientSize.Width/scale);store.Settings.WindowHeight=(int)Math.Round(ClientSize.Height/scale);Save();}Push();}};
        FormClosing += (s, e) => {
            if (!quitting && !verifying && !liveVerify) { e.Cancel = true; resizePrompt=false;if(openCodeAttempt!=null){string cancelPath="/api/integration/"+Uri.EscapeDataString(openCodeAttemptProvider)+"/connect/oauth/"+Uri.EscapeDataString(openCodeAttempt);openCodeAttempt=null;openCodeAttemptProvider=null;Task.Run(async()=>{try{await openCode.Api(cancelPath,"DELETE");}catch{}});} Hide(); DisposeView(); }
            else { renewalTimer.Stop(); updateTimer.Stop(); refreshTimer.Stop(); reloadTimer.Stop(); if (login != null) login.Dispose(); if (watcher != null) watcher.Dispose(); if (authWatcher != null) authWatcher.Dispose(); DisposeView();localProxy.Dispose();openCode.Dispose(); tray.Dispose(); }
        };
        if ((!test && !live) || hot) Task.Run(() => ControlServer());
    }
    private async Task ControlServer() {
        try {
            while (!quitting) {
                PipeSecurity security = new PipeSecurity(); security.SetAccessRuleProtection(true, false); security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                using (NamedPipeServerStream pipe = new NamedPipeServerStream(hotVerify ? "CommandCodeManagerVerifyControl" : "CommandCodeManagerControl", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security)) {
                    await pipe.WaitForConnectionAsync(); string command = await new StreamReader(pipe).ReadLineAsync(); if(hotVerify)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","control-trace.txt"),"command: ["+command+"]"+Environment.NewLine);
                    if (command == "show") BeginInvoke(new Action(async () => await ShowWindow()));
                    if (command == "hide") BeginInvoke(new Action(() => { Hide(); DisposeView(); }));
                    if (command == "quit") { BeginInvoke(new Action(() => { quitting = true; Close(); })); return; }
                    if (command == "update") { while (busy || addingAccount || switching.Count > 0 || login != null || openCodeAttempt!=null) await Task.Delay(250); BeginInvoke(new Action(() => { if(hotVerify)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","control-trace.txt"),"closing for update"+Environment.NewLine); Restart = true; quitting = true; Close(); })); return; }
                }
            }
        } catch (Exception error) { if(hotVerify)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","control-trace.txt"),error.ToString()+Environment.NewLine); }
    }
    private async Task ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); await InitView(); Push(); if(!verifying&&!liveVerify){Task ignored=updater.CheckAutomatically(appVersion);} }
    private ToolStripMenuItem TrayAction(string text, Func<Task> action, bool enabled = true) {
        ToolStripMenuItem item = new ToolStripMenuItem(text) { Enabled = enabled };
        item.Click += async (s,e) => { try { await action(); } catch(Exception error) { tray.BalloonTipTitle="多账号工作台"; tray.BalloonTipText=error.Message; tray.ShowBalloonTip(5000); } };
        return item;
    }
    private void BuildTrayMenu(ContextMenuStrip menu) {
        while(menu.Items.Count>0) { ToolStripItem old=menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose(); }
        Account current=store.Accounts.FirstOrDefault(a=>a.Id==activeId);
        menu.Items.Add(new ToolStripMenuItem("当前账号："+(current==null?"未匹配":current.Name)) { Enabled=false });
        AccountData quota=current==null?null:current.Data;
        menu.Items.Add(new ToolStripMenuItem("剩余额度："+(quota==null?"暂无数据":Core.Money(quota.Remaining))));
        menu.Items.Add(new ToolStripMenuItem("订阅周期已用："+(quota==null?"暂无数据":Core.Money(quota.MonthlyUsed)+" / "+Core.Money(quota.MonthlyTotal))));
        menu.Items.Add(new ToolStripMenuItem("5 小时已用："+TrayWindowQuota(quota==null?null:quota.FiveHour,quota)));
        menu.Items.Add(new ToolStripMenuItem("每周已用："+TrayWindowQuota(quota==null?null:quota.Weekly,quota)));
        if(current!=null&&!string.IsNullOrEmpty(current.Error))menu.Items.Add(new ToolStripMenuItem("同步异常，以上为最近记录"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayAction("刷新当前账号",()=>RefreshOne(current,new HistoryQuery{Grain="day",Page=0}),current!=null&&!busy&&!transferBusy&&refreshingAccounts.Count==0));
        menu.Items.Add(TrayAction("刷新并检测全部账号",RefreshAll,store.Accounts.Count>0&&!busy&&!transferBusy&&refreshingAccounts.Count==0));
        ToolStripMenuItem accounts=new ToolStripMenuItem("快速切换账号");
        foreach(Account account in store.Accounts) {
            Account target=account; ToolStripMenuItem item=TrayAction((OpenCodeService.IsOpenCode(target)?"OpenCode · ":"Command Code · ")+target.Name+" · "+Core.CredentialLabel(target.CredentialState),async()=>{await SwitchAccount(target);tray.BalloonTipTitle="多账号工作台 · 账号已启用";tray.BalloonTipText=target.Name+"，新开的 "+(OpenCodeService.IsOpenCode(target)?"OpenCode":"Command Code")+" 会话生效";tray.ShowBalloonTip(3000);},target.Id!=activeId&&!openCodeActive.Contains(target.Id)&&target.CredentialState!="invalid"&&target.Data!=null&&!transferBusy&&switching.Count==0&&(OpenCodeService.IsOpenCode(target)||string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMMAND_CODE_API_KEY"))));
            item.Checked=target.Id==activeId||openCodeActive.Contains(target.Id);accounts.DropDownItems.Add(item);
        }
        accounts.Enabled=store.Accounts.Count>0;menu.Items.Add(accounts);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayAction("彻底退出",()=>{quitting=true;Close();return Task.FromResult(0);}));
    }
    private static string TrayWindowQuota(WindowUsage window,AccountData data) {
        if(data!=null&&data.Limited==false)return "不限窗口";
        if(window==null)return "暂无数据";
        return Core.Money(window.Used)+" / "+Core.Money(window.Cap);
    }
    private static string ClientExecutable(bool openCode) {string folder=openCode?"@opencode-aidesktop":"Command Code",name=openCode?"OpenCode.exe":"Command Code.exe";return new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+"\\Programs",Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Select(root=>Path.Combine(root,folder,name)).FirstOrDefault(File.Exists);}
    private async Task RestartCommandCode() {
        string exe=ClientExecutable(false);if(exe==null)throw new InvalidOperationException("未找到 Command Code 官方客户端");
        var targets=new List<Process>();foreach(Process candidate in Process.GetProcessesByName("Command Code")){try{if(string.Equals(candidate.MainModule.FileName,exe,StringComparison.OrdinalIgnoreCase))targets.Add(candidate);else candidate.Dispose();}catch{candidate.Dispose();}}
        try {foreach(Process target in targets)try{if(!target.HasExited)target.CloseMainWindow();}catch(InvalidOperationException){}await Task.Delay(1500);foreach(Process target in targets)try{if(!target.HasExited){target.Kill();if(!target.WaitForExit(5000))throw new InvalidOperationException("Command Code 尚未退出，请手动完全退出后重新打开");}}catch(InvalidOperationException){if(!target.HasExited)throw;}Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});}finally{foreach(Process target in targets)target.Dispose();}
    }
    private async Task SwitchAccount(Account account) {
        if(!OpenCodeService.IsOpenCode(account)&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMMAND_CODE_API_KEY")))throw new InvalidOperationException("环境变量覆盖登录配置，请清除 COMMAND_CODE_API_KEY 后再切换");
        if(transferBusy||switching.Count>0)throw new InvalidOperationException("账号操作正在进行，请稍后重试");
        switching.Add(account.Id);Push();
        if(OpenCodeService.IsOpenCode(account)){try{account.OpenCodeCredentialId=await openCode.Switch(account,keys[account.Id]);Save();IdentifyActive();}finally{switching.Remove(account.Id);Push();}return;}
        try { string key=keys[account.Id];Dictionary<string,object> identity=await Task.Run(()=>Core.Get(key,"/alpha/whoami","limits=1"));string userId=Core.Text(Core.Object(Core.Value(identity,"user")),"id"),org=Core.Text(Core.Object(Core.Value(identity,"org")),"id");if(account.Data==null||userId!=account.Data.UserId||org!=account.Data.OrgId)throw new InvalidOperationException("账号或组织身份变化，已停止切换");Core.Switch(authFile,key,account.Data);activeId=account.Id;SyncOfficial(); }
        finally {switching.Remove(account.Id);Push();}
    }
    protected override CreateParams CreateParams { get { CreateParams parameters = base.CreateParams; parameters.Style = (parameters.Style | 0x40000 | 0x10000 | 0x20000 | 0x80000) & ~0xC00000; return parameters; } }
    protected override void WndProc(ref Message message) {
        if (message.Msg == 0x83 && message.WParam != IntPtr.Zero) { message.Result = IntPtr.Zero; return; }
        base.WndProc(ref message);
        if (message.Msg == 0x84 && WindowState == FormWindowState.Normal && message.Result == new IntPtr(1)) { long position = message.LParam.ToInt64(); Point p = PointToClient(new Point((short)(position & 65535), (short)((position >> 16) & 65535))); int border = 7; int hit = p.X < border ? p.Y < border ? 13 : p.Y >= Height-border ? 16 : 10 : p.X >= Width-border ? p.Y < border ? 14 : p.Y >= Height-border ? 17 : 11 : p.Y < border ? 2 : p.Y >= Height-border ? 15 : 1; message.Result = new IntPtr(hit); }
    }
    private void DisposeView() { ready = false; if (view != null) { Controls.Remove(view); view.Dispose(); view = null; } }
    private async Task InitView() {
        if (view != null || initializing) return; initializing = true;
        try {
            view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(220, 230, 243) }; Controls.Add(view);
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommandCodeManager", verifying || liveVerify ? "webview-verify" : "webview");
            if (environment == null) environment = await CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-component-update"));
            await view.EnsureCoreWebView2Async(environment);
            view.ZoomFactor = 1;
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("commandcode.local", deployedDir, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.Settings.AreDevToolsEnabled = verifying || liveVerify; view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; view.CoreWebView2.Settings.IsStatusBarEnabled = false; view.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            view.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
            view.CoreWebView2.NavigationStarting += (s, e) => { if (!e.Uri.StartsWith("https://commandcode.local/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            view.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;
            view.CoreWebView2.WebMessageReceived += async (s, e) => await Message(e);
            view.CoreWebView2.NavigationCompleted += async (s, e) => { ready = e.IsSuccess; if (ready) { navigations++; Push(); if (hotVerify) { string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification"); Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "hot-ready.json"), new JavaScriptSerializer().Serialize(new { pid = Process.GetCurrentProcess().Id, domain = AppDomain.CurrentDomain.Id, ready = true, navigations = navigations })); } else if (verifying || liveVerify) await Verify(); } };
            view.CoreWebView2.Navigate("https://commandcode.local/index.html");
        } catch (Exception e) { if (verifying || liveVerify) { Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification")); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification", "startup-error.txt"), e.ToString()); quitting = true; Close(); } else { MessageBox.Show(e is WebView2RuntimeNotFoundException ? "此电脑缺少 Microsoft Edge WebView2 运行环境，请按使用说明安装后重新打开程序。" : "界面加载失败：" + e.Message, "多账号工作台"); quitting=true;Close(); } }
        finally { initializing = false; }
    }
    private sealed class ProxyVerificationHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){string json=request.Method==HttpMethod.Get?"{\"object\":\"list\",\"data\":[{\"id\":\"fixture-open\",\"supported_endpoints\":[\"/chat/completions\"]}]}":"{\"choices\":[{\"message\":{\"content\":\"fixture response\"}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":5}}";return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json")});}
    }
    private ProxyAccount[] ProxyAccounts(){lock(gate)return store.Accounts.Where(a=>keys.ContainsKey(a.Id)).Select(a=>{bool oc=OpenCodeService.IsOpenCode(a);string provider=oc?(a.ProviderId=="opencode-go"?"opencode-go":a.ProviderId=="opencode"?"opencode-zen":"unsupported"):"commandcode";string key=oc?Core.Text(OpenCodeService.ParseValue(keys[a.Id]),"key"):keys[a.Id];var candidate=new ProxyAccount{Id=a.Id,Name=a.Name,Key=key,Provider=provider,CredentialState=a.CredentialState,Balance=a.Data==null?null:a.Data.Remaining,ApiSupported=provider!="unsupported"&&!string.IsNullOrWhiteSpace(key)&&(oc||a.Data==null||!IsCommandCodeGo(a.Data.Plan)),QuotaUntil=a.Data==null?null:ProxyReset(a.Data)};LocalProxy.ApplyQuota(candidate,a.Data,DateTimeOffset.UtcNow);return candidate;}).ToArray();}
    private static bool IsCommandCodeGo(string plan){return System.Text.RegularExpressions.Regex.IsMatch(plan??"",@"^(?:individual-|teams-)?go(?:-v\d+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);}
    private static DateTimeOffset? ProxyReset(AccountData data){DateTimeOffset parsed;DateTimeOffset? latest=null;var windows=new[]{data.FiveHour,data.Weekly,new WindowUsage{Used=data.MonthlyUsed,Cap=data.MonthlyTotal,ResetAt=string.IsNullOrEmpty(data.MonthlyResetAt)?data.PeriodEnd:data.MonthlyResetAt}};foreach(var window in windows){if(window==null||!window.Used.HasValue||!window.Cap.HasValue||window.Cap<=0||window.Used<window.Cap)continue;if(Core.TryTime(window.ResetAt,out parsed)&&parsed>DateTimeOffset.UtcNow&&(!latest.HasValue||parsed>latest))latest=parsed;}return latest;}
    private void ConfigureProxy(Dictionary<string,object> payload){if(localProxy.InFlight>0)throw new InvalidOperationException("代理请求正在进行，完成后再修改设置");int port=(int)(Core.Number(payload,"port")??17766);bool enabled=Core.Boolean(payload,"enabled")==true;var list=Core.Value(payload,"accountIds") as System.Collections.IEnumerable;if(list==null)throw new InvalidOperationException("请选择账号池");var ids=list.Cast<object>().Select(Convert.ToString).Distinct().ToList();if(ids.Any(id=>!ProxyAccounts().Any(a=>a.Id==id&&a.ApiSupported)))throw new InvalidOperationException("请选择支持 API Key 的 Command Code、OpenCode Go 或 Zen 账号");var previous=store.Proxy;string oldKey=string.IsNullOrEmpty(previous.Secret)?null:Core.Decrypt(previous.Secret),key=oldKey??LocalProxy.GenerateKey();string bind=Core.Text(payload,"bindAddress");if(string.IsNullOrEmpty(bind))bind="127.0.0.1";var next=new ProxySettings{BindAddress=bind,Port=port,Enabled=enabled,Secret=Core.Encrypt(key),AccountIds=ids};localProxy.Configure(port,key,ids,enabled,bind);store.Proxy=next;try{Save();}catch{store.Proxy=previous;localProxy.Configure(previous.Port,oldKey,previous.AccountIds,previous.Enabled,previous.BindAddress??"127.0.0.1");throw;}}
    private object State() {
        lock (gate) return new { accounts = store.Accounts.Select(a => new { id = a.Id, platform=OpenCodeService.IsOpenCode(a)?"opencode":"commandcode",providerId=a.ProviderId,providerName=OpenCodeService.IsOpenCode(a)?OpenCodeService.ProviderName(a.ProviderId):"CommandCode", name = a.Name, renewalAt=a.RenewalAt, refreshing = refreshingAccounts.Contains(a.Id), credentialState = a.CredentialState, credentialLabel = Core.CredentialLabel(a.CredentialState), credentialCheckedAt = a.CredentialCheckedAt, credentialMessage = a.CredentialMessage, officialUsagePanel = a.OfficialUsagePanel, method = a.Method, active = a.Id == activeId || openCodeActive.Contains(a.Id), switching = switching.Contains(a.Id), plan = OpenCodeService.IsOpenCode(a)?OpenCodeService.ProviderName(a.ProviderId):Core.PlanLabel(a.Data == null ? "" : a.Data.Plan), rawPlan = a.Data == null ? "" : a.Data.Plan, status = Core.StatusLabel(a.Data == null ? "" : a.Data.Status), error = a.Error, data = a.Data, alerts = Core.Alerts(a, store.Settings, DateTimeOffset.UtcNow).Select(value => value.Text).ToArray() }).ToArray(), update=new{status=updater.Status,latest=updater.Latest,progress=updater.Progress,error=updater.Error,notes=updater.Notes,publishedAt=updater.PublishedAt}, appVersion=appVersion, proxy=localProxy.State(), resizePrompt=resizePrompt, maximized = WindowState==FormWindowState.Maximized, refreshPaused = refreshPaused, singleRefreshing = refreshingAccounts.Count>0, transferBusy = transferBusy, settings = store.Settings, officialUsage = OfficialUsage.Status(), historySyncing = runningHistory.Count>0, refreshing = busy, completed = completed, total = store.Accounts.Count, loggingIn = login != null, environmentOverride = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMMAND_CODE_API_KEY")) };
    }
    private void Send(object message) { if (ready && view != null) view.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(message)); }
    private void Push() { Send(new { kind = "state", state = State() }); }
    private void Save() { if (verifying || liveVerify) return; lock (gate) Core.AtomicWrite(file, new JavaScriptSerializer {MaxJsonLength=8388608}.Serialize(store)); }
    private void SyncOfficial() {
        if (verifying || liveVerify) return;
        Account active = store.Accounts.FirstOrDefault(a => a.Id == activeId); bool enabled = active != null && active.OfficialUsagePanel&&!OpenCodeService.IsOpenCode(active); string userId = enabled && active.Data != null ? active.Data.UserId + "|" + active.Data.OrgId : ""; string signature = enabled + "|" + userId;
        if (signature != officialSignature) { OfficialUsage.WriteState(enabled, userId); officialSignature = signature; }
    }
    private void InstallOfficial() {
        Account active = store.Accounts.FirstOrDefault(a => a.Id == activeId); bool enabled = active != null && active.OfficialUsagePanel&&!OpenCodeService.IsOpenCode(active);
        OfficialUsage.Apply(store.Accounts.Any(a=>a.OfficialUsagePanel&&!OpenCodeService.IsOpenCode(a)), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addons"), enabled, enabled && active.Data != null ? active.Data.UserId + "|" + active.Data.OrgId : ""); officialSignature = null; SyncOfficial();
    }
    private void IdentifyActive() {
        activeId = null;
        try { if (File.Exists(authFile)) { string key = Core.Text(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(authFile)), "apiKey"); activeId = keys.Where(pair => pair.Value == key).Select(pair => pair.Key).FirstOrDefault(); } } catch { }
        SyncOfficial();
        openCodeActive.Clear();try{var active=OpenCodeService.Credentials().Where(item=>item.Active).ToArray();foreach(Account account in store.Accounts.Where(OpenCodeService.IsOpenCode)){string secret;if(keys.TryGetValue(account.Id,out secret)&&active.Any(item=>item.Provider==account.ProviderId&&(item.Id==account.OpenCodeCredentialId&&!string.IsNullOrEmpty(item.Id)||OpenCodeService.Fingerprint(item.Value)==OpenCodeService.Fingerprint(secret))))openCodeActive.Add(account.Id);}}catch{}
    }
    private void Schedule() { refreshTimer.Stop(); refreshTimer.Interval = Math.Max(60, Math.Min(3600, store.Settings.Interval)) * 1000; if (store.Accounts.Count > 0 && !verifying && !liveVerify && !refreshPaused) refreshTimer.Start(); }
    private static string HistoryJob(string accountId,HistoryQuery query) { return accountId+"|"+query.Grain+"|"+query.Page+"|"+query.Start+"|"+query.End; }
    private void EnqueueHistory(Account account,HistoryQuery query) {
        if(account==null||OpenCodeService.IsOpenCode(account)||verifying||liveVerify||quitting||!queuedHistory.Add(HistoryJob(account.Id,query))) return;
        List<HistoryQuery> pending; if(!pendingHistory.TryGetValue(account.Id,out pending)) pendingHistory[account.Id]=pending=new List<HistoryQuery>(); pending.Add(query);
    }
    private void WarmHistory(Account account) {if(account!=null&&OpenCodeService.IsOpenCode(account))return;
        foreach(string grain in new string[]{"day","week","month"}) EnqueueHistory(account,new HistoryQuery{Grain=grain,Page=0});
        StartHistory(account);
    }
    private void QueueHistory(Account account,string grain,int page,bool force=false) { EnqueueHistory(account,new HistoryQuery{Grain=grain,Page=page,Force=force}); StartHistory(account); }
    private void QueueHistory(Account account,HistoryQuery query) {EnqueueHistory(account,query);StartHistory(account);}
    private void StartHistory(Account account) {
        if(account==null||OpenCodeService.IsOpenCode(account)||verifying||liveVerify||quitting||!pendingHistory.ContainsKey(account.Id)||!runningHistory.Add(account.Id)) return;
        Push(); ProcessHistory(account);
    }
    private async void ProcessHistory(Account account) {
        try {
            List<HistoryQuery> pending;
            while(!quitting&&pendingHistory.TryGetValue(account.Id,out pending)&&pending.Count>0) {
                HistoryQuery[] batch=pending.ToArray(); pending.Clear(); HistoryWindow[] results=null; string error=null;
                try {
                    string key; lock(gate) keys.TryGetValue(account.Id,out key); if(key==null) throw new InvalidOperationException("账号已移除或凭据无法解密");
                    string org=account.Data==null?"":account.Data.OrgId;
                    results=await Task.Run(()=>historyCache.Refresh(account.Id,key,org,batch,DateTimeOffset.UtcNow));
                } catch(Exception e) { error=e.Message; }
                foreach(HistoryQuery query in batch) queuedHistory.Remove(HistoryJob(account.Id,query));
                if(quitting) break;
                if(!store.Accounts.Contains(account)) { try { historyCache.Remove(account.Id); } catch { } break; }
                if(results==null) results=batch.Select(q=>historyCache.Read(account.Id,q,DateTimeOffset.UtcNow)).ToArray();
                foreach(HistoryWindow result in results) SendHistory(result,error??result.Error,false);
            }
        } catch(Exception e) {
            List<HistoryQuery> abandoned; if(pendingHistory.TryGetValue(account.Id,out abandoned)) { foreach(HistoryQuery q in abandoned) queuedHistory.Remove(HistoryJob(account.Id,q)); abandoned.Clear(); }
            Send(new {kind="historyError",accountId=account.Id,error=e.Message});
        } finally { runningHistory.Remove(account.Id); pendingHistory.Remove(account.Id); if(!quitting) Push(); }
    }
    private void SendHistory(HistoryWindow window,string error,bool updating) { Send(new {kind="history",accountId=window.AccountId,grain=window.Grain,page=window.Page,start=window.Start,end=window.End,pages=window.Pages,hasNext=window.HasNext,history=window.Rows,historyAt=window.UpdatedAt,historyError=error,updating=updating}); }
    private HistoryQuery Query(Dictionary<string,object> payload) {HistoryQuery q=new HistoryQuery{Grain=Core.Text(payload,"grain"),Page=(int)(Core.Number(payload,"page")??0),Start=Core.Text(payload,"start"),End=Core.Text(payload,"end"),Force=Core.Boolean(payload,"refresh")==true};int pages;TokenHistoryCache.Dates(q,DateTimeOffset.UtcNow,out pages);return q;}
    private async Task UpdateAccount(Account account) {
        string key;lock(gate)keys.TryGetValue(account.Id,out key);
        try {
            if(key==null)throw new InvalidOperationException("凭据无法解密，请重新导入账号");
            AccountData data=OpenCodeService.IsOpenCode(account)?await openCode.Fetch(account,key):await Task.Run(()=>Core.Fetch(key));AccountData old=account.Data;
            bool same=old!=null&&old.UserId==data.UserId&&old.OrgId==data.OrgId;
            if(old!=null&&!same&&!verifying&&!liveVerify)historyCache.Remove(account.Id);
            if(same&&!data.TotalTokens.HasValue&&!string.IsNullOrEmpty(data.TokenError)){data.TotalTokens=old.TotalTokens;data.InputTokens=old.InputTokens;data.OutputTokens=old.OutputTokens;}
            lock(gate){account.Data=data;account.Error=null;account.CredentialState=OpenCodeService.IsOpenCode(account)&&data.Status=="configured"?"configured":(data.Error??"").Contains("访问权限")||(data.TokenError??"").Contains("访问权限")?"limited":"valid";account.CredentialCheckedAt=DateTimeOffset.UtcNow.ToString("o");account.CredentialMessage=OpenCodeService.IsOpenCode(account)&&data.Status=="configured"?"凭据已配置；有效性由 OpenCode 官方授权或请求结果确认":string.IsNullOrEmpty(data.Error)?"账号凭据验证通过":data.Error;}
            Notify(account);
        } catch(Exception e) {lock(gate){account.Error=e.Message;account.CredentialState=Core.CredentialFailure(e);account.CredentialCheckedAt=DateTimeOffset.UtcNow.ToString("o");account.CredentialMessage=e.Message;}if(!string.IsNullOrEmpty(account.RenewalAt))Notify(account);throw;}
    }
    private async Task RefreshOne(Account account,HistoryQuery query) {
        if(busy||!refreshingAccounts.Add(account.Id))throw new InvalidOperationException("账号正在刷新，请稍后重试");Push();
        try {await UpdateAccount(account);Save();if(OpenCodeService.IsOpenCode(account)){SendHistory(await Task.Run(()=>openCode.History(account,query)),null,false);}else{query.Force=true;EnqueueHistory(account,query);WarmHistory(account);}}
        finally {refreshingAccounts.Remove(account.Id);Save();Push();}
    }
    private async Task RefreshAll() {
        if (busy || refreshingAccounts.Count>0 || store.Accounts.Count == 0) return; IdentifyActive(); busy = true; completed = 0; Push();
        try {
            using (SemaphoreSlim slots = new SemaphoreSlim(2)) {
                Account[] accounts; lock (gate) accounts = store.Accounts.ToArray();
                await Task.WhenAll(accounts.Select(async account => {
                    await slots.WaitAsync();
                    try {await UpdateAccount(account);}
                    catch { }
                    finally { slots.Release(); completed++; Push(); }
                }));
            }
            Save(); foreach(Account account in store.Accounts.ToArray()) if(account.Data!=null&&string.IsNullOrEmpty(account.Error)) WarmHistory(account);
        } finally { busy = false; Push(); }
    }
    private void Notify(Account account) {
        List<Alert> alerts = Core.Alerts(account, store.Settings, DateTimeOffset.UtcNow); List<Alert> fresh = alerts.Where(value => account.AlertIds == null || !account.AlertIds.Contains(value.Id)).ToList(); account.AlertIds = alerts.Select(value => value.Id).ToList();
        if (fresh.Count > 0 && store.Settings.Notifications && !verifying && !liveVerify) { tray.BalloonTipTitle = "多账号工作台 · " + account.Name; tray.BalloonTipText = string.Join("；", fresh.Select(value => value.Text)); tray.ShowBalloonTip(5000); }
    }
    private Account Find(string id) { Account result = store.Accounts.FirstOrDefault(a => a.Id == id); if (result == null) throw new InvalidOperationException("账号不存在"); return result; }
    private static string TransferKey(string platform,string provider,string value){return platform=="opencode"?"opencode|"+provider+"|"+OpenCodeService.Fingerprint(value):"commandcode|"+value;}
    private async Task<bool> AddOpenCode(string provider,string value,string name,string credentialId=null){provider=OpenCodeService.ValidateProvider(provider);value=new JavaScriptSerializer().Serialize(OpenCodeService.ParseValue(value));if(store.Accounts.Any(account=>OpenCodeService.IsOpenCode(account)&&account.ProviderId==provider&&keys.ContainsKey(account.Id)&&((!string.IsNullOrEmpty(credentialId)&&account.OpenCodeCredentialId==credentialId)||OpenCodeService.Fingerprint(keys[account.Id])==OpenCodeService.Fingerprint(value))))return false;if(store.Accounts.Count>=200)throw new InvalidOperationException("最多管理 200 个账号");Send(new{kind="addProgress",message="正在读取 OpenCode 用量…"});var accountToAdd=new Account{Platform="opencode",ProviderId=provider,OpenCodeCredentialId=credentialId,Id=Guid.NewGuid().ToString("N"),Name=string.IsNullOrWhiteSpace(name)?OpenCodeService.ProviderName(provider):name.Substring(0,Math.Min(name.Length,80)),Method=Core.Text(OpenCodeService.ParseValue(value),"type"),Secret=Core.Encrypt(value),OfficialUsagePanel=false};accountToAdd.Data=await openCode.Fetch(accountToAdd,value);accountToAdd.CredentialState=accountToAdd.Data.Status=="active"?"valid":"configured";accountToAdd.CredentialCheckedAt=DateTimeOffset.UtcNow.ToString("o");lock(gate){store.Accounts.Add(accountToAdd);keys[accountToAdd.Id]=value;}Save();Schedule();IdentifyActive();Push();return true;}
    private async Task<string> ImportOpenCode(string provider,string remark){if(addingAccount)throw new InvalidOperationException("账号正在导入，请稍候");addingAccount=true;try{var selected=(await Task.Run(()=>OpenCodeService.Credentials())).Where(item=>string.IsNullOrEmpty(provider)||item.Provider==provider).ToArray();if(selected.Length==0)throw new InvalidOperationException("未找到该服务商的本机登录，请先完成 OpenCode 官方授权");int count=0;foreach(var credential in selected)if(await AddOpenCode(credential.Provider,credential.Value,string.IsNullOrWhiteSpace(remark)?credential.Label:remark,credential.Id))count++;return count==0?"账号已在列表中，无需重复导入":"已导入 "+count+" 个 OpenCode 账号";}finally{addingAccount=false;}}
    private async Task Add(string key, string name, string method) {
        if(addingAccount)throw new InvalidOperationException("账号正在添加，请稍候");
        addingAccount=true;
        try {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Contains("\n") || key.Contains("\r")) throw new InvalidOperationException("请填写有效 API Key"); key = key.Trim();
        if(keys.Values.Any(value=>value==key))throw new AccountAlreadyExistsException();
        AccountData data = await Core.Fetch(key,value=>Send(new{kind="addProgress",message=value}),identity=>{
            string userId=Core.Text(Core.Object(Core.Value(identity,"user")),"id"),orgId=Core.Text(Core.Object(Core.Value(identity,"org")),"id");
            if(store.Accounts.Any(account=>account.Data!=null&&account.Data.UserId==userId&&account.Data.OrgId==orgId))throw new AccountAlreadyExistsException();
        });
        Send(new{kind="addProgress",message="正在保存账号…"});
        lock (gate) {
            if (store.Accounts.Any(a => a.Data != null && a.Data.UserId == data.UserId && a.Data.OrgId == data.OrgId)) throw new AccountAlreadyExistsException();
            Account account = new Account { Id = Guid.NewGuid().ToString("N"), Name = string.IsNullOrWhiteSpace(name) ? data.Email : name.Substring(0, Math.Min(name.Length, 80)), Secret = Core.Encrypt(key), Data = data, Method = method,CredentialState="valid",CredentialCheckedAt=DateTimeOffset.UtcNow.ToString("o") }; keys[account.Id] = key; store.Accounts.Add(account); Notify(account);
        }
        Save(); Schedule(); Push(); WarmHistory(store.Accounts.FirstOrDefault(a=>a.Data!=null&&a.Data.UserId==data.UserId&&a.Data.OrgId==data.OrgId));
        } finally {addingAccount=false;}
    }
    private async Task<string> ExportAccounts(Dictionary<string,object> payload) {
        if(transferBusy)throw new InvalidOperationException("账号包操作正在进行");
        var values=Core.Value(payload,"accountIds") as System.Collections.IEnumerable;if(values==null||values is string)throw new InvalidOperationException("请选择需要导出的账号");
        Account[] chosen=values.Cast<object>().Select(v=>Find(Convert.ToString(v))).Distinct().ToArray();if(chosen.Length==0)throw new InvalidOperationException("至少选择一个账号");
        PortableAccount[] accounts=chosen.Select(a=>{string key;if(!keys.TryGetValue(a.Id,out key))throw new InvalidOperationException("无法读取 "+a.Name+" 的凭据，请取消勾选");return new PortableAccount{RenewalAt=a.RenewalAt,Platform=a.Platform,ProviderId=a.ProviderId,Name=a.Name,ApiKey=key,Method=a.Method,UserId=a.Data==null?"":a.Data.UserId,OrgId=a.Data==null?"":a.Data.OrgId,UserName=a.Data==null?"":a.Data.UserName,Email=a.Data==null?"":a.Data.Email,OfficialUsagePanel=a.OfficialUsagePanel};}).ToArray();
        string password=Core.Text(payload,"password");if(password.Length<8)throw new InvalidOperationException("请设置至少 8 位账号包密码");
        using(SaveFileDialog dialog=new SaveFileDialog {Title="导出加密账号包",Filter="加密账号包 (*.ccaccounts)|*.ccaccounts",DefaultExt="ccaccounts",AddExtension=true,FileName="多账号工作台账号包.ccaccounts",RestoreDirectory=true}) {
            if(dialog.ShowDialog(this)!=DialogResult.OK)return "已取消导出";if(Path.GetExtension(dialog.FileName).ToLowerInvariant()!=".ccaccounts")throw new InvalidOperationException("请保存为 .ccaccounts 账号包");
            transferBusy=true;Push();try {string encrypted=await Task.Run(()=>AccountTransfer.Encrypt(accounts,password));Core.AtomicWrite(dialog.FileName,encrypted);return "已导出 "+accounts.Length+" 个账号，使用此文件和密码即可在其他电脑导入";} finally{transferBusy=false;Push();}
        }
    }
    private async Task<string> ImportAccounts(string password) {
        if(transferBusy||busy||refreshingAccounts.Count>0)throw new InvalidOperationException("请等待当前账号操作完成");if(string.IsNullOrEmpty(importFile))throw new InvalidOperationException("请先选择账号包文件");
        transferBusy=true;Push();
        try {
            string path=importFile;PortableAccount[] imported=await Task.Run(()=>{if(new FileInfo(path).Length>5242880)throw new InvalidOperationException("账号包文件过大");return AccountTransfer.Decrypt(File.ReadAllText(path),password);});
            List<PortableAccount> fresh=new List<PortableAccount>();HashSet<string> knownKeys=new HashSet<string>(store.Accounts.Where(a=>keys.ContainsKey(a.Id)).Select(a=>TransferKey(a.Platform,a.ProviderId,keys[a.Id])));HashSet<string> knownIdentity=new HashSet<string>(store.Accounts.Where(a=>!OpenCodeService.IsOpenCode(a)&&a.Data!=null&&!string.IsNullOrEmpty(a.Data.UserId)).Select(a=>a.Data.UserId+"|"+a.Data.OrgId));int skipped=0;
            foreach(PortableAccount a in imported){string key=TransferKey(a.Platform,a.ProviderId,a.ApiKey.Trim()),identity=(a.UserId??"")+"|"+(a.OrgId??"");if(knownKeys.Contains(key)||a.Platform!="opencode"&&!string.IsNullOrEmpty(a.UserId)&&knownIdentity.Contains(identity)){skipped++;continue;}knownKeys.Add(key);if(a.Platform!="opencode"&&!string.IsNullOrEmpty(a.UserId))knownIdentity.Add(identity);fresh.Add(a);}
            if(store.Accounts.Count+fresh.Count>200)throw new InvalidOperationException("管理账号数量最多 200 个");
            Account[] prepared=await Task.Run(()=>fresh.Select(a=>new Account{RenewalAt=a.RenewalAt,Platform=a.Platform,ProviderId=a.ProviderId,Id=Guid.NewGuid().ToString("N"),Name=string.IsNullOrWhiteSpace(a.Name)?string.IsNullOrWhiteSpace(a.Email)?"导入账号":a.Email:a.Name,Secret=Core.Encrypt(a.ApiKey.Trim()),Method=a.Method=="password"||a.Method=="api"?a.Method:"import",OfficialUsagePanel=a.Platform!="opencode"&&a.OfficialUsagePanel,CredentialState="unknown",Data=new AccountData{UserId=a.UserId,OrgId=a.OrgId,UserName=a.UserName,Email=a.Email}}).ToArray());
            lock(gate){for(int i=0;i<prepared.Length;i++){store.Accounts.Add(prepared[i]);keys[prepared[i].Id]=fresh[i].ApiKey.Trim();}}
            try{Save();}catch{lock(gate){foreach(Account a in prepared){store.Accounts.Remove(a);keys.Remove(a.Id);}}throw;}
            importFile=null;Schedule();Push();if(prepared.Length>0)BeginInvoke(new Action(async()=>{try{await RefreshAll();}catch(Exception e){Send(new{kind="notice",message="账号已导入，后台检测暂未完成："+e.Message});}}));
            return "已导入 "+prepared.Length+" 个账号，跳过 "+skipped+" 个重复账号；正在后台检测";
        } finally {transferBusy=false;Push();}
    }
    private async Task<string> ExportUsage(Account account,HistoryQuery query) {
        DateTimeOffset now=DateTimeOffset.UtcNow;int pages;TokenHistoryCache.Dates(query,now,out pages);
        using(SaveFileDialog dialog=new SaveFileDialog {Title="导出用量明细",Filter="CSV 文件 (*.csv)|*.csv",DefaultExt="csv",AddExtension=true,FileName="CommandCode用量-"+DateTime.Now.ToString("yyyyMMdd")+".csv",RestoreDirectory=true}) {
            if(dialog.ShowDialog(this)!=DialogResult.OK)return "已取消导出";if(Path.GetExtension(dialog.FileName).ToLowerInvariant()!=".csv")throw new InvalidOperationException("请保存为 CSV 文件");
            HistoryQuery[] queries=string.IsNullOrEmpty(query.Start)?new[]{query}:Enumerable.Range(0,pages).Select(p=>new HistoryQuery{Grain=query.Grain,Page=p,Start=query.Start,End=query.End}).ToArray();
            HistoryWindow[] windows=OpenCodeService.IsOpenCode(account)?await Task.Run(()=>queries.Select(q=>openCode.History(account,q)).ToArray()):queries.Select(q=>historyCache.Read(account.Id,q,now)).ToArray();
            if(windows.Any(w=>!w.Complete)) {string key;if(!keys.TryGetValue(account.Id,out key))throw new InvalidOperationException("凭据无法解密，请重新导入账号");windows=await Task.Run(()=>historyCache.Refresh(account.Id,key,account.Data==null?"":account.Data.OrgId,queries,now,(done,total)=>{if(!quitting)try{BeginInvoke(new Action(()=>Send(new{kind="exportProgress",accountId=account.Id,completed=done,total=total})));}catch(InvalidOperationException){}}));}
            if(windows.Any(w=>!w.Complete||!string.IsNullOrEmpty(w.Error)))throw new InvalidOperationException("部分明细尚未获取完整，已保留本地记录，请稍后重试");
            Core.AtomicWrite(dialog.FileName,UsageCsv.Create(account,windows.SelectMany(w=>w.Rows).ToArray()));return "用量明细已导出，共 "+windows.Sum(w=>w.Rows.Length)+" 条";
        }
    }
    private async Task Message(CoreWebView2WebMessageReceivedEventArgs e) {
        if (!e.Source.StartsWith("https://commandcode.local/", StringComparison.OrdinalIgnoreCase)) return;
        Dictionary<string, object> request; try { request = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
        string id = Core.Text(request, "id"), op = Core.Text(request, "op"); Dictionary<string, object> payload = Core.Object(Core.Value(request, "payload")); string message = "";
        try {
            if ((verifying || liveVerify) && op != "ready" && op != "state" && op != "maximize" && op != "minimize" && op != "history" && !(verifying&&(op=="pageSize"||op=="restoreDefaultSize"||op=="keepWindowSize"||op=="renewal"||op=="officialUsage"||op=="proxyConfigure"||op=="proxyKey"||op=="proxyStop"))) throw new InvalidOperationException("检查模式不修改真实账号");
            switch (op) {
                case "minimize": WindowState = FormWindowState.Minimized; break;
                case "maximize": MaximizedBounds = Screen.FromControl(this).WorkingArea; WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                case "closeWindow": Close(); break;
                case "proxyConfigure": ConfigureProxy(payload);message=store.Proxy.Enabled?"本地代理已开启，其他软件可使用统一地址和代理 Key":"本地代理已关闭";break;
                case "proxyRefreshAccount": string proxyId=Core.Text(payload,"accountId");if(!store.Accounts.Any(proxyRow=>proxyRow.Id==proxyId))throw new InvalidOperationException("账号不存在");await RefreshOne(Find(proxyId),new HistoryQuery{Grain="day",Page=0});if(store.Accounts.First(proxyRow=>proxyRow.Id==proxyId).CredentialState!="invalid"&&string.IsNullOrEmpty(store.Accounts.First(proxyRow=>proxyRow.Id==proxyId).Error))localProxy.RecoverAccount(proxyId);message="账号额度已重新检测";break;
                case "proxyClearCooldown":localProxy.ClearCooldown();message="已清除冷却记录，后续请求会重新尝试所选账号";break;
                case "proxyStop":bool proxyWasEnabled=store.Proxy.Enabled;store.Proxy.Enabled=false;try{Save();}catch{store.Proxy.Enabled=proxyWasEnabled;throw;}localProxy.Stop();message="本地代理已停止，进行中的请求已取消";break;
                case "proxyKey": if(string.IsNullOrEmpty(store.Proxy.Secret))throw new InvalidOperationException("请先保存代理设置以生成 Key");Send(new{kind="reply",id=id,ok=true,proxyKey=Core.Decrypt(store.Proxy.Secret),state=State()});return;
                case "proxyRotateKey": if(localProxy.InFlight>0)throw new InvalidOperationException("请求正在进行，请完成后再更换 Key");string oldProxySecret=store.Proxy.Secret;string nextProxyKey=LocalProxy.GenerateKey();store.Proxy.Secret=Core.Encrypt(nextProxyKey);try{Save();}catch{store.Proxy.Secret=oldProxySecret;throw;}localProxy.Configure(store.Proxy.Port,nextProxyKey,store.Proxy.AccountIds,store.Proxy.Enabled,store.Proxy.BindAddress??"127.0.0.1");message="代理 Key 已更换，旧 Key 的新请求立即失效";break;
                case "viewUpdates": await updater.Check(appVersion); message=updater.Status=="current"?"已经是最新版本":"";break;
                case "downloadUpdate": await updater.Download();break;
                case "installUpdate": if(busy||addingAccount||transferBusy||refreshingAccounts.Count>0||switching.Count>0||login!=null||openCodeAttempt!=null||runningHistory.Count>0||localProxy.InFlight>0)throw new InvalidOperationException("当前操作尚未完成，请稍后安装");Save();updater.Install();if(File.Exists(Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),"unins000.exe"))){quitting=true;Close();}break;
                case "ready": case "state": break;
                case "restoreDefaultSize": resizePrompt=false;WindowState=FormWindowState.Normal;ClientSize=defaultClientSize;if(!verifying&&!liveVerify){double restoreScale=GetDpiForWindow(Handle)/96.0;store.Settings.WindowWidth=(int)Math.Round(ClientSize.Width/restoreScale);store.Settings.WindowHeight=(int)Math.Round(ClientSize.Height/restoreScale);Save();}Push();break;
                case "keepWindowSize": resizePrompt=false;Push();break;
                case "pageSize": int pageSize=(int)(Core.Number(payload,"value")??-1);if(!new[]{0,3,5,6,8,10}.Contains(pageSize))throw new InvalidOperationException("请选择有效的每页数量");store.Settings.PageSize=pageSize;Save();Push();break;
                case "refresh": await RefreshAll(); break;
                case "openCodeAdd": if(addingAccount)throw new InvalidOperationException("账号正在添加，请稍候");addingAccount=true;try{message=await AddOpenCode(Core.Text(payload,"provider"),OpenCodeService.KeyValue(Core.Text(payload,"key")),Core.Text(payload,"name"))?"OpenCode 账号已添加":"账号已在列表中，无需重复添加";}finally{addingAccount=false;}break;
                case "openCodeImport": message=await ImportOpenCode(OpenCodeService.ValidateProvider(Core.Text(payload,"provider")),Core.Text(payload,"name"));break;
                case "openCodeConsole": Process.Start(new ProcessStartInfo("https://opencode.ai/auth"){UseShellExecute=true});message="网页登录后复制 API Key，在管理器中添加；不保存网页密码";break;
                case "openCodeAuthMethods": var integrations=Core.Value(await openCode.Api("/api/integration"),"data") as System.Collections.IEnumerable;string integrationId=OpenCodeService.ValidateProvider(Core.Text(payload,"provider"));var integration=integrations==null?new Dictionary<string,object>():integrations.Cast<object>().Select(Core.Object).FirstOrDefault(item=>Core.Text(item,"id")==integrationId)??new Dictionary<string,object>();var methods=Core.Value(integration,"methods") as System.Collections.IEnumerable;Send(new{kind="reply",id=id,ok=true,authMethods=methods==null?new object[0]:methods.Cast<object>().Select(Core.Object).Where(method=>Core.Text(method,"type")=="oauth").Select(method=>new{id=Core.Text(method,"id"),label=Core.Text(method,"label"),form=Core.Value(method,"form")}).ToArray(),state=State()});return;
                case "openCodeOAuthBegin": if(openCodeAttempt!=null)throw new InvalidOperationException("请先完成或取消当前官方授权");openCodeAttemptProvider=OpenCodeService.ValidateProvider(Core.Text(payload,"provider"));var attempt=Core.Object(Core.Value(await openCode.Api("/api/integration/"+Uri.EscapeDataString(openCodeAttemptProvider)+"/connect/oauth","POST",new{methodID=Core.Text(payload,"methodId"),label=Core.Text(payload,"name"),answer=Core.Object(Core.Value(payload,"answer"))}),"data"));openCodeAttempt=Core.Text(attempt,"attemptID");Uri authUrl;if(!Uri.TryCreate(Core.Text(attempt,"url"),UriKind.Absolute,out authUrl)||(authUrl.Scheme!="https"&&!(authUrl.Scheme=="http"&&authUrl.IsLoopback)))throw new InvalidOperationException("官方授权地址无效");Process.Start(new ProcessStartInfo(authUrl.AbsoluteUri){UseShellExecute=true});Send(new{kind="reply",id=id,ok=true,authMode=Core.Text(attempt,"mode"),instructions=Core.Text(attempt,"instructions"),state=State()});return;
                case "openCodeOAuthStatus": if(openCodeAttempt==null)throw new InvalidOperationException("没有进行中的官方授权");var authStatus=Core.Object(Core.Value(await openCode.Api("/api/integration/"+Uri.EscapeDataString(openCodeAttemptProvider)+"/connect/oauth/"+Uri.EscapeDataString(openCodeAttempt)),"data"));string outcome=Core.Text(authStatus,"status");if(outcome=="complete"){string provider=openCodeAttemptProvider;openCodeAttempt=null;openCodeAttemptProvider=null;message=await ImportOpenCode(provider,Core.Text(payload,"name"));}Send(new{kind="reply",id=id,ok=true,authStatus=outcome,message=message,instructions=Core.Text(authStatus,"message"),state=State()});return;
                case "openCodeOAuthComplete": if(openCodeAttempt==null)throw new InvalidOperationException("没有进行中的官方授权");await openCode.Api("/api/integration/"+Uri.EscapeDataString(openCodeAttemptProvider)+"/connect/oauth/"+Uri.EscapeDataString(openCodeAttempt)+"/complete","POST",new{code=Core.Text(payload,"code")});break;
                case "detectAccounts": await RefreshAll(); message="账号检测完成："+store.Accounts.Count(value=>value.CredentialState=="valid"||value.CredentialState=="limited")+" 个可用，"+store.Accounts.Count(value=>value.CredentialState=="invalid")+" 个失效，其余状态可在账号详情查看";break;
                case "refreshAccount": await RefreshOne(Find(Core.Text(payload,"accountId")),Query(payload));message="此账号的余额、Token 和历史正在更新";break;
                case "exportAccounts": message=await ExportAccounts(payload);break;
                case "chooseAccountFile": if(transferBusy)throw new InvalidOperationException("账号包操作正在进行");using(OpenFileDialog dialog=new OpenFileDialog{Title="选择加密账号包",Filter="加密账号包 (*.ccaccounts)|*.ccaccounts",CheckFileExists=true,RestoreDirectory=true}){if(dialog.ShowDialog(this)==DialogResult.OK){importFile=dialog.FileName;Send(new{kind="reply",id=id,ok=true,fileName=Path.GetFileName(importFile),state=State()});return;}}message="已取消选择";break;
                case "importAccounts": message=await ImportAccounts(Core.Text(payload,"password"));break;
                case "exportUsage": message=await ExportUsage(Find(Core.Text(payload,"accountId")),Query(payload));break;
                case "add": await Add(Core.Text(payload, "key"), Core.Text(payload, "name"), "api"); break;
                case "import": if (!File.Exists(authFile)) throw new InvalidOperationException("未发现本机登录账号"); await Add(Core.Text(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(authFile)), "apiKey"), Core.Text(payload, "name"), "import"); break;
                case "login":
                    if (login != null) throw new InvalidOperationException("请先完成当前登录");
                    using (login = new LoginFlow()) { Push(); try { Task<string> waiting = login.Wait(); Process.Start(new ProcessStartInfo(login.Url) { UseShellExecute = true }); await Add(await waiting, Core.Text(payload, "name"), "password"); } finally { login = null; Push(); } } break;
                case "cancelLogin": if (login != null) login.Dispose();if(openCodeAttempt!=null){try{await openCode.Api("/api/integration/"+Uri.EscapeDataString(openCodeAttemptProvider)+"/connect/oauth/"+Uri.EscapeDataString(openCodeAttempt),"DELETE");}finally{openCodeAttempt=null;openCodeAttemptProvider=null;}}break;
                case "launch": Account launchAccount=Find(Core.Text(payload,"id"));bool launchOpenCode=OpenCodeService.IsOpenCode(launchAccount);string launchPath=ClientExecutable(launchOpenCode);if(launchPath==null)throw new InvalidOperationException("未找到 "+(launchOpenCode?"OpenCode":"Command Code")+" 官方客户端，请先安装");Process.Start(new ProcessStartInfo(launchPath){UseShellExecute=true});message="已打开 "+(launchOpenCode?"OpenCode":"Command Code")+"，当前登录以官方客户端为准";break;
                case "switch":
                    if (!OpenCodeService.IsOpenCode(Find(Core.Text(payload,"id")))&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMMAND_CODE_API_KEY"))) throw new InvalidOperationException("环境变量覆盖登录配置，请清除 COMMAND_CODE_API_KEY 后再切换");
                    Account a = Find(Core.Text(payload, "id")); await SwitchAccount(a); message = "已切换为 " + a.Name + (OpenCodeService.IsOpenCode(a)?"，新开的 OpenCode 会话生效；已有会话未更新时请重新打开 OpenCode。":"，新开的 CommandCode 会话生效。"); break;
                case "remove": if (busy||transferBusy||refreshingAccounts.Count>0||localProxy.InFlight>0) throw new InvalidOperationException("刷新进行中，请稍后移除"); string removeId = Core.Text(payload, "id"); Find(removeId); lock (gate) { store.Accounts.RemoveAll(value => value.Id == removeId); keys.Remove(removeId); } if(store.Proxy.AccountIds.Remove(removeId)){store.Proxy.Enabled=store.Proxy.Enabled&&store.Proxy.AccountIds.Count>0;localProxy.Configure(store.Proxy.Port,string.IsNullOrEmpty(store.Proxy.Secret)?null:Core.Decrypt(store.Proxy.Secret),store.Proxy.AccountIds,store.Proxy.Enabled,store.Proxy.BindAddress??"127.0.0.1");} try { historyCache.Remove(removeId); } catch { } Save(); SyncOfficial(); Schedule(); Push(); break;
                case "renewal": Account renewal=Find(Core.Text(payload,"id"));string renewalValue=Core.Text(payload,"value").Trim();DateTimeOffset renewalTime=DateTimeOffset.MinValue;if(renewalValue!=""&&(!DateTimeOffset.TryParse(renewalValue,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out renewalTime)||renewalTime.Year<2020||renewalTime.Year>2100))throw new InvalidOperationException("请输入有效的续费日期");string previousRenewal=renewal.RenewalAt;renewal.RenewalAt=renewalValue==""?"":renewalTime.ToString("o");try{Save();}catch{renewal.RenewalAt=previousRenewal;throw;}Notify(renewal);Save();Push();message=renewalValue==""?"已清除手动续费日期":"续费日期已保存，将提前 7 / 3 / 1 天提醒";break;
                case "rename": Account rename = Find(Core.Text(payload, "id")); string name = Core.Text(payload, "name").Trim(); if (name == "") throw new InvalidOperationException("请填写账号备注"); rename.Name = name.Substring(0, Math.Min(name.Length, 80)); Save(); Push(); break;
                case "settings":
                    int interval = (int)(Core.Number(payload, "interval") ?? 0), warning = (int)(Core.Number(payload, "warning") ?? 0), critical = (int)(Core.Number(payload, "critical") ?? 0); double low = Core.Number(payload, "lowBalance") ?? -1;
                    if (interval < 60 || interval > 3600 || warning < 1 || warning >= critical || critical > 100 || low < 0) throw new InvalidOperationException("刷新需为 60–3600 秒，预警阈值需小于严重预警");
                    store.Settings = new Settings { PageSize=store.Settings.PageSize,WindowWidth=store.Settings.WindowWidth,WindowHeight=store.Settings.WindowHeight, Interval = interval, Warning = warning, Critical = critical, LowBalance = low, Notifications = Core.Boolean(payload, "notifications") == true, OfficialUsagePanel = false };
                    Save(); Schedule(); Push(); break;
                case "history":
                    Account historyAccount=Find(Core.Text(payload,"accountId"));HistoryQuery historyQuery=Query(payload);string grain=historyQuery.Grain;int page=historyQuery.Page;DateTimeOffset historyNow=DateTimeOffset.UtcNow;int pages;DateTimeOffset[] boundaries=TokenHistoryCache.Dates(historyQuery,historyNow,out pages);
                    if(verifying){TokenPeriod[] rows=boundaries.Take(boundaries.Length-1).Select((d,i)=>new TokenPeriod{Start=d.ToString("o"),End=boundaries[i+1].ToString("o"),Tokens=15000+i*100,Input=14000+i*100,Output=1000,Requests=5,Cost=.15}).Reverse().ToArray();Send(new{kind="reply",id=id,ok=true,accountId=historyAccount.Id,grain=grain,page=page,start=historyQuery.Start,end=historyQuery.End,pages=pages,hasNext=page+1<pages,history=rows,historyAt=historyNow.ToString("o"),updating=false,state=State()});return;}
                    if(OpenCodeService.IsOpenCode(historyAccount)){var opencodeHistory=await Task.Run(()=>openCode.History(historyAccount,historyQuery));Send(new{kind="reply",id=id,ok=true,accountId=historyAccount.Id,grain=grain,page=page,start=historyQuery.Start,end=historyQuery.End,pages=opencodeHistory.Pages,hasNext=opencodeHistory.HasNext,history=opencodeHistory.Rows,historyAt=opencodeHistory.UpdatedAt,updating=false,state=State()});return;}HistoryWindow local=historyCache.Read(historyAccount.Id,historyQuery,historyNow);bool update=historyQuery.Force||!local.Complete||queuedHistory.Contains(HistoryJob(historyAccount.Id,historyQuery));
                    Send(new{kind="reply",id=id,ok=true,accountId=historyAccount.Id,grain=grain,page=page,start=historyQuery.Start,end=historyQuery.End,pages=local.Pages,hasNext=local.HasNext,history=local.Rows,historyAt=local.UpdatedAt,historyError=local.Error,updating=update,state=State()});
                    if(update&&!liveVerify)QueueHistory(historyAccount,historyQuery);return;
                case "restartCommandCode": if(Core.Boolean(payload,"confirmed")!=true)throw new InvalidOperationException("请先确认重启 Command Code");await RestartCommandCode();message="Command Code 已重新打开，用量面板在所选账号启用时显示";break;
                case "officialUsage":
                    Account panelAccount = Find(Core.Text(payload,"accountId"));if(OpenCodeService.IsOpenCode(panelAccount))throw new InvalidOperationException("OpenCode 不支持客户端用量面板"); bool wasPanelInstalled=!verifying&&OfficialUsage.IsInstalled();bool oldPanel = panelAccount.OfficialUsagePanel; panelAccount.OfficialUsagePanel=Core.Boolean(payload,"enabled")==true;
                    try { if(!verifying&&!liveVerify)InstallOfficial(); Save(); } catch { panelAccount.OfficialUsagePanel=oldPanel; throw; }
                    message=panelAccount.OfficialUsagePanel?"已开启此账号的客户端用量面板":"已关闭此账号的客户端用量面板"; Push();Send(new{kind="reply",id=id,ok=true,message=message,requiresClientReload=panelAccount.OfficialUsagePanel&&!wasPanelInstalled,state=State()});return;
                default: throw new InvalidOperationException("不支持的操作");
            }
            Send(new { kind = "reply", id = id, ok = true, message = message, state = State() });
        } catch (AccountAlreadyExistsException) { Send(new { kind="reply",id=id,ok=true,alreadyExists=true,message="账号已在列表中，无需重复添加",state=State() }); }
        catch (Exception error) { Send(new { kind = "reply", id = id, ok = false, error = error.Message, state = State() }); }
    }
    private void Seed() {
        for (int i = 0; i < 14; i++) store.Accounts.Add(new Account { Id = "demo-" + i, Name = new string[] { "主力研发账号", "备用编码账号", "团队协作账号" }[i % 3] + (i > 2 ? " " + (i + 1) : ""), Secret = Core.Encrypt("fixture-" + i), Method = "api", CredentialState="valid", CredentialCheckedAt=DateTimeOffset.UtcNow.ToString("o"), Data = new AccountData { UserId = "demo", Email = "demo" + i + "@example.com", Plan = "individual-goat", Status = "active", Remaining = 56.56 - i, Monthly = 56.56 - i, Purchased = 0, Free = 0, MonthlyUsed = 13.44 + i, MonthlyTotal = 70, TotalTokens = 90575648, InputTokens = 90300000, OutputTokens = 275648, PeriodTokens = 85258665, PeriodStart = "2026-10-13T06:35:41Z", PeriodEnd = "2026-11-13T06:35:41Z", FetchedAt = DateTimeOffset.UtcNow.ToString("o"), Limited = true, FiveHour = new WindowUsage { Used = 13.44, Cap = 14, ResetAt = DateTimeOffset.UtcNow.AddHours(2).ToString("o") }, Weekly = new WindowUsage { Used = 13.44, Cap = 35, ResetAt = i == 2 ? "0" : DateTimeOffset.UtcNow.AddDays(4).ToString("o") } } });
        activeId = "demo-0";
    }
    private async Task Verify() {
        try {
            await Task.Delay(1000); if (liveVerify && busy) { while (busy) await Task.Delay(100); }
            await view.CoreWebView2.ExecuteScriptAsync("window.__startLatencyProbe && window.__startLatencyProbe()"); await Task.Delay(1200);
            string check = await view.CoreWebView2.ExecuteScriptAsync("window.__verifyUI ? window.__verifyUI() : {passed:false,error:'React 未加载'}");
            string latency = await view.CoreWebView2.ExecuteScriptAsync("window.__latencyResult || null"); Dictionary<string, object> report = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(check); report["interactionLatency"] = new JavaScriptSerializer().DeserializeObject(latency); check = new JavaScriptSerializer().Serialize(report);
            await view.CoreWebView2.ExecuteScriptAsync("window.__startHistoryLatencyProbe && window.__startHistoryLatencyProbe()"); await Task.Delay(500);
            report["historyCacheLatency"]=new JavaScriptSerializer().DeserializeObject(await view.CoreWebView2.ExecuteScriptAsync("window.__historyLatencyResult || null")); check=new JavaScriptSerializer().Serialize(report);
            if (verifying) {
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.proxy-open-button').click()");await Task.Delay(100);
                bool proxyDialog=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.proxy-modal')&&document.querySelector('[aria-label=\"本地代理地址\"]').value==='http://127.0.0.1:17766/v1'&&document.querySelectorAll('.proxy-account-list tbody tr').length>=3&&document.querySelector('.proxy-account-list thead').textContent.includes('每月可用')&&!!document.querySelector('[aria-label=\"代理访问范围\"]')&&!document.querySelector('.proxy-connect').textContent.includes('不影响 VPN')&&Array.from(document.querySelectorAll('.proxy-account-list tbody tr')).every(row=>row.querySelector('.ant-checkbox').getBoundingClientRect().width<20&&row.querySelector('b').getBoundingClientRect().width>70)&&document.querySelector('.proxy-modal').getBoundingClientRect().bottom<=innerHeight-5&&document.querySelector('.proxy-modal').scrollHeight<=document.querySelector('.proxy-modal').clientHeight+1&&document.querySelector('.proxy-modal').scrollWidth<=document.querySelector('.proxy-modal').clientWidth+1") == "true";
                using(FileStream preview=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","proxy-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.proxy-account-list tbody input').click()");await Task.Delay(50);
                bool proxySelection=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.proxy-account-list tbody input').checked&&!document.querySelector('.proxy-controls .primary').disabled") == "true";
                var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int testPort=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
                await view.CoreWebView2.ExecuteScriptAsync("(()=>{const input=document.querySelector('[aria-label=\"代理端口\"]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'"+testPort+"');input.dispatchEvent(new Event('input',{bubbles:true}))})()");await Task.Delay(60);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.proxy-controls .primary').click()");await Task.Delay(150);bool proxyStarted=localProxy.Running&&store.Proxy.Enabled;
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"本地代理 Key\"]').parentElement.querySelectorAll('button')[1].click()");await Task.Delay(80);
                bool proxyKey=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"本地代理 Key\"]').value.startsWith('mw_')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"本地代理 Key\"]').parentElement.querySelectorAll('button')[1].click()");
                using(var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(10)}){client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",Core.Decrypt(store.Proxy.Secret));for(int sample=0;sample<4;sample++)using(var response=await client.PostAsync("http://127.0.0.1:"+testPort+"/v1/chat/completions",new StringContent("{\"model\":\"fixture-open\"}",System.Text.Encoding.UTF8,"application/json"))){if(!response.IsSuccessStatusCode)throw new InvalidOperationException("代理界面集成请求失败："+(int)response.StatusCode+" "+await response.Content.ReadAsStringAsync());}}
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.proxy-view-tabs button')[1].click()");await Task.Delay(120);bool proxyLogs=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.proxy-records tbody tr').length>=2&&document.querySelector('.proxy-records').textContent.includes('12 / 5')&&document.querySelector('.proxy-modal').getBoundingClientRect().bottom<=innerHeight-5") == "true";
                using(FileStream preview=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","proxy-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,preview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.proxy-view-tabs button')[0].click()");await Task.Delay(80);using(FileStream poolPreview=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","proxy-pool-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,poolPreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.proxy-controls button')[1].click()");await Task.Delay(80);bool proxyStopped=!localProxy.Running&&!store.Proxy.Enabled;
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()");report["localProxyUI"]=new{dialog=proxyDialog,accountSelection=proxySelection,starts=proxyStarted,key=proxyKey,logs=proxyLogs,stops=proxyStopped};if(!proxyDialog||!proxySelection||!proxyStarted||!proxyKey||!proxyLogs||!proxyStopped)throw new InvalidOperationException("本地代理界面验证失败："+new JavaScriptSerializer().Serialize(report["localProxyUI"]));
                OnResizeBegin(EventArgs.Empty);OnResizeEnd(EventArgs.Empty);bool moveIgnored=!resizePrompt;
                OnResizeBegin(EventArgs.Empty);ClientSize=new Size(defaultClientSize.Width-30,defaultClientSize.Height);OnResizeEnd(EventArgs.Empty);await Task.Delay(100);
                bool resizeShown=resizePrompt&&await view.CoreWebView2.ExecuteScriptAsync("(()=>{const p=document.querySelector('.resize-prompt');return !!p&&p.getBoundingClientRect().top<85})()") == "true";
                Size keptSize=ClientSize;await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.resize-prompt button').click()");await Task.Delay(100);bool kept=!resizePrompt&&ClientSize==keptSize;
                OnResizeBegin(EventArgs.Empty);ClientSize=new Size(defaultClientSize.Width-20,defaultClientSize.Height);OnResizeEnd(EventArgs.Empty);await Task.Delay(100);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.resize-prompt .primary').click()");await Task.Delay(100);bool restoredDefault=!resizePrompt&&ClientSize==defaultClientSize;
                report["resizeNotice"]=new{shownAtTop=resizeShown,moveIgnored=moveIgnored,keepsCurrent=kept,restoresDefault=restoredDefault,versionVisible=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.footer-version').textContent") == new JavaScriptSerializer().Serialize("v"+appVersion)};
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.page-size-button').click()");await Task.Delay(80);
                bool sizeMenuOpen=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.page-size-menu')&&!document.querySelector('.page-size-control select')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page-size=\"3\"]').click()");await Task.Delay(120);
                bool sizeSelected=store.Settings.PageSize==3&&await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.account-row').length===3&&!document.querySelector('.page-size-menu')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.page-size-button').click()");await Task.Delay(80);await view.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");await Task.Delay(50);
                bool sizeDismissed=await view.CoreWebView2.ExecuteScriptAsync("!document.querySelector('.page-size-menu')") == "true";
                report["pageSizeControl"]=new{opens=sizeMenuOpen,selects=sizeSelected,dismisses=sizeDismissed,withPager=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.pagination .page-size-control')") == "true"};store.Settings.PageSize=0;Push();await Task.Delay(100);
                Stopwatch duplicateTimer=Stopwatch.StartNew();bool duplicateBlocked=false;
                try {await Add(keys[store.Accounts[0].Id],"重复测试","import");}catch(InvalidOperationException error){duplicateBlocked=error.Message.Contains("无需重复导入");}
                duplicateTimer.Stop();report["duplicateImport"]=new{blockedLocally=duplicateBlocked,elapsedMs=duplicateTimer.Elapsed.TotalMilliseconds,operationUnlocked=!addingAccount};
                BuildTrayMenu(tray.ContextMenuStrip);
                ToolStripMenuItem quickSwitch=tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First(item=>item.Text=="快速切换账号");
                bool accountMenu=quickSwitch.DropDownItems.Count==store.Accounts.Count;
                report["trayMenu"]=new{accountMenu=accountMenu,quotaShown=tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Count(item=>item.Text.Contains("已用：")||item.Text.StartsWith("剩余额度："))==4,simpleMenu=!tray.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Any(item=>item.Text.Contains("自动刷新")||item.Text=="打开账号总览"||item.Text=="隐藏到托盘"||item.Text=="桌面预警提醒")};
                bool topPopups = true;
                foreach (string selector in new string[] { ".header-actions .primary", ".header-actions .settings-button", ".identity" }) {
                    await view.CoreWebView2.ExecuteScriptAsync("document.querySelector(" + new JavaScriptSerializer().Serialize(selector) + ").click()"); await Task.Delay(80);
                    string top = await view.CoreWebView2.ExecuteScriptAsync("(()=>{const modal=document.querySelector('.modal');return !!modal&&modal.getBoundingClientRect().top<85})()"); topPopups &= top == "true";
                    if (selector == ".identity") { topPopups &= await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal h2').textContent==='用量统计'&&!document.querySelector('.account-modal-tabs')") == "true"; }
                    await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()"); await Task.Delay(50);
                }
                report["popupsAtTop"] = topPopups;
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.identity').click()"); await Task.Delay(200);
                report["tokenHistoryTable"] = new JavaScriptSerializer().DeserializeObject(await view.CoreWebView2.ExecuteScriptAsync("(()=>{const m=document.querySelector('.usage-modal');const t=document.querySelector('.history-table');return {rows:document.querySelectorAll('.history-table tbody tr').length,noModalScroll:m.scrollHeight<=m.clientHeight+1,noHorizontalScroll:t.scrollWidth<=t.clientWidth+1,noAccountSelector:!m.querySelector('select'),chineseUnits:/[万亿]/.test(m.querySelector('.token-summary').textContent),atTop:m.getBoundingClientRect().top<85}})()"));
                string historyOutput = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification"); Directory.CreateDirectory(historyOutput); using(FileStream historyImage=File.Create(Path.Combine(historyOutput,"token-history-preview.png"))) await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,historyImage);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()"); await Task.Delay(50);
                int normalRows=int.Parse(await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.account-row').length"));
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"最大化或还原\"]').click()"); await Task.Delay(250); bool maximized = WindowState == FormWindowState.Maximized;
                int maximizedRows=int.Parse(await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.account-row').length"));
                store.Settings.PageSize=3;Push();await Task.Delay(150);bool manualRows=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.account-row').length===3") == "true";store.Settings.PageSize=0;Push();await Task.Delay(100);
                report["pageLayout"]=new{normalRows=normalRows,maximizedRows=maximizedRows,manualRows=manualRows,updateCentered=await view.CoreWebView2.ExecuteScriptAsync("(()=>{const b=document.querySelector('.footer-release').getBoundingClientRect();return Math.abs((b.left+b.right)/2-innerWidth/2)<5})()") == "true"};
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"最大化或还原\"]').click()"); await Task.Delay(250); bool restored = WindowState == FormWindowState.Normal;
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"最小化\"]').click()"); await Task.Delay(150); bool minimized = WindowState == FormWindowState.Minimized; WindowState = FormWindowState.Normal; await Task.Delay(150);
                report["windowButtonsWork"] = maximized && restored && minimized; report["nativeTitleBarRemoved"] = FormBorderStyle == FormBorderStyle.None; report["dragRegionEnabled"] = view.CoreWebView2.Settings.IsNonClientRegionSupportEnabled; check = new JavaScriptSerializer().Serialize(report);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.header-actions .primary').click()");await Task.Delay(80);await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.platform-trigger').click()");await Task.Delay(80);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.platform-options button')[1].click()");await Task.Delay(80);
                string addPreviewDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream addPreview=File.Create(Path.Combine(addPreviewDir,"add-account-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,addPreview);
                bool openCodeKeyOnly=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.method-tabs button').length===0&&document.querySelector('.api-key-help').textContent.includes('API Key')&&!!document.querySelector('.platform-trigger .platform-logo')") == "true";
                bool openCodeAdd=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.provider-field select')&&document.querySelector('.modal').getBoundingClientRect().top<85") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()");await Task.Delay(50);
                var demoOpenCode=new Account{Id="fixture-opencode",Name="OpenCode Go 测试账号",Platform="opencode",ProviderId="opencode-go",Method="key",CredentialState="valid",Data=new AccountData{Plan="opencode-go",Status="active",UserName="OpenCode Go",Email="OpenCode Go",UsageUnit="percent",MonthlyUsed=80,MonthlyTotal=100,MonthlyResetAt=DateTimeOffset.UtcNow.AddDays(3).ToString("o"),Limited=true,TotalTokens=1618121229,InputTokens=162276031,OutputTokens=1693070,ReasoningTokens=3705536,CacheReadTokens=1450446592,CacheWriteTokens=0,FiveHour=new WindowUsage{Used=10,Cap=100,ResetAt=DateTimeOffset.UtcNow.AddHours(2).ToString("o")},Weekly=new WindowUsage{Used=30,Cap=100,ResetAt=DateTimeOffset.UtcNow.AddDays(2).ToString("o")},Models=new[]{new ModelUsage{Model="fixture-model",Tokens=1618121229,Input=162276031,Output=1693070,CacheRead=1450446592,Reasoning=3705536,Requests=1000,Cost=1.25}},UsageNote="本机服务商历史统计，未按账号区分"}};
                store.Accounts.Insert(0,demoOpenCode);Push();await Task.Delay(100);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.proxy-open-button').click()");await Task.Delay(100);report["openCodeProxyUI"]=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.proxy-account-list').textContent.includes('OpenCode Go 测试账号')&&!document.querySelector('.proxy-account-list tbody input').disabled") == "true";await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()");await Task.Delay(50);bool noOpenCodePanel=await view.CoreWebView2.ExecuteScriptAsync("!document.querySelector('[data-platform=\"opencode\"] input[type=checkbox]')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.platform-tabs button')[2].click()");await Task.Delay(100);bool onlyOpenCode=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.account-row').length===1&&document.querySelector('.account-row').dataset.platform==='opencode'&&document.querySelector('.stat-grid strong').textContent.includes('1')") == "true";
                bool openCodeAtTop=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.account-row').getBoundingClientRect().top-document.querySelector('.table-head').getBoundingClientRect().bottom<3") == "true";
                bool remainingQuota=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.account-row .usage strong').textContent==='80%'&&document.querySelector('.account-row .usage-top span').textContent==='已用 · 可用 20%'") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.account-row').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:innerWidth-20,clientY:innerHeight-20}))");await Task.Delay(200);
                bool contextMenu=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.account-context-menu')&&document.querySelector('.account-context-menu').textContent.includes('在 OpenCode 中使用')&&!document.querySelector('.account-context-menu').textContent.includes('客户端用量面板')&&Array.from(document.querySelectorAll('.account-context-menu .ant-dropdown-menu-item')).length>=9&&document.querySelector('.account-context-menu').getBoundingClientRect().right<=innerWidth+1&&document.querySelector('.account-context-menu').getBoundingClientRect().bottom<=innerHeight+1") == "true";
                using(FileStream menuPreview=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification","context-menu-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,menuPreview);
                await view.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('.account-context-menu .ant-dropdown-menu-item')).find(e=>e.textContent.includes('修改账号别名')).click()");await Task.Delay(100);
                bool contextRename=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.rename-form input')&&document.querySelector('.rename-form input').value==='OpenCode Go 测试账号'") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click();document.querySelector('.account-row').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:500,clientY:400}))");await Task.Delay(150);
                await view.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('.account-context-menu .ant-dropdown-menu-item')).find(e=>e.textContent.includes('导出此账号')).click()");await Task.Delay(100);
                bool contextExport=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.transfer-list input:checked').length===1&&document.querySelector('.transfer-list input:checked').closest('label').textContent.includes('OpenCode Go 测试账号')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()");await Task.Delay(100);
                report["accountContextMenu"]=new{opensAtEdge=contextMenu,renameCorrectAccount=contextRename,exportsOnlySelected=contextExport};
                if(!contextMenu||!contextRename||!contextExport)throw new InvalidOperationException("账号右键菜单验证失败");
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"卡片面板\"]').click()");await Task.Delay(150);bool cardLayout=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.card-grid')&&Array.from(document.querySelectorAll('.account-row')).every(r=>r.getBoundingClientRect().bottom<=document.querySelector('.table-shell').getBoundingClientRect().bottom+1)&&document.querySelector('.table-shell').scrollWidth<=document.querySelector('.table-shell').clientWidth+1") == "true";
                string cardsDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream cardPreview=File.Create(Path.Combine(cardsDir,"cards-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,cardPreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"列表面板\"]').click()");await Task.Delay(100);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.platform-tabs button')[1].click()");await Task.Delay(100);bool onlyCommandCode=await view.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('.account-row')).every(row=>row.dataset.platform==='commandcode')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"卡片面板\"]').click()");await Task.Delay(150);bool multiCardLayout=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.account-row').length>1&&Array.from(document.querySelectorAll('.account-row')).every(r=>Array.from(r.children).every(c=>c.getBoundingClientRect().bottom<=r.getBoundingClientRect().bottom+1)&&r.getBoundingClientRect().bottom<=document.querySelector('.table-shell').getBoundingClientRect().bottom+1)") == "true";
                string multiCardsDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream multiCardPreview=File.Create(Path.Combine(multiCardsDir,"multi-cards-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,multiCardPreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"列表面板\"]').click()");await Task.Delay(100);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.platform-tabs button')[0].click()");await Task.Delay(100);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-platform=\"opencode\"] .identity').click()");await Task.Delay(200);
                bool openCodeStats=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.opencode-summary>div').length===6&&!document.querySelector('.usage-modal select')&&document.querySelector('.usage-modal').scrollHeight<=document.querySelector('.usage-modal').clientHeight+1") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.history-range .ant-picker-input input').click()");await Task.Delay(150);bool rangeCalendar=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.ant-picker-presets')&&document.querySelector('.ant-picker-presets').textContent.includes('本月')&&document.querySelector('.ant-picker-dropdown').textContent.includes('年')") == "true";
                string rangePreviewDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream rangePreview=File.Create(Path.Combine(rangePreviewDir,"range-calendar-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,rangePreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click();document.querySelector('[data-platform=\"opencode\"]').dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,clientX:500,clientY:400}))");await Task.Delay(100);await view.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('.account-context-menu .ant-dropdown-menu-item')).find(e=>e.textContent.includes('账号详情')).click()");await Task.Delay(100);bool renewalEditor=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.renewal-editor input')&&document.querySelector('.modal').scrollHeight<=document.querySelector('.modal').clientHeight+1") == "true";
                string manualValue=DateTimeOffset.UtcNow.AddDays(20).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");await view.CoreWebView2.ExecuteScriptAsync("window.chrome.webview.postMessage({id:'fixture-renewal',op:'renewal',payload:{id:'fixture-opencode',value:"+new JavaScriptSerializer().Serialize(manualValue)+"}})");await Task.Delay(150);bool renewalSaved=!string.IsNullOrEmpty(demoOpenCode.RenewalAt);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.renewal-picker input').click()");await Task.Delay(150);bool renewalCalendar=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.ant-picker-time-panel')&&document.querySelector('.ant-picker-dropdown').textContent.includes('此刻')") == "true";
                string calendarDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.renewal-editor').click()");await Task.Delay(50);bool insideKeeps=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.modal')") == "true";
                string detailPreviewDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream detailPreview=File.Create(Path.Combine(detailPreviewDir,"renewal-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,detailPreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-backdrop').click()");await Task.Delay(100);bool outsideCloses=await view.CoreWebView2.ExecuteScriptAsync("!document.querySelector('.modal-backdrop')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.platform-tabs button')[1].click()");await Task.Delay(80);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.account-row .account-panel-toggle input:not(:checked)').click()");await Task.Delay(150);
                bool panelRestartPrompt=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.restart-client')&&document.querySelector('.restart-client').textContent.includes('工作台不用重启')") == "true";
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.restart-client .confirm-actions button').click()");await Task.Delay(60);
                bool independentDialogs=await view.CoreWebView2.ExecuteScriptAsync("!document.querySelector('.modal')&&!document.querySelector('.launch-button')&&!document.querySelector('.account-modal-tabs')") == "true";
                report["separateAccountActions"]=new{panelRestartPrompt=panelRestartPrompt,independentDialogs=independentDialogs};if(!panelRestartPrompt||!independentDialogs)throw new InvalidOperationException("独立弹窗及客户端重启提示验证失败：prompt="+panelRestartPrompt+",dialogs="+independentDialogs);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.platform-tabs button')[0].click()");await Task.Delay(80);
                report["workspaceUI"]=new{openCodeAtTop=openCodeAtTop,remainingQuota=remainingQuota,cardLayout=cardLayout,multiCardLayout=multiCardLayout,renewalCalendar=renewalCalendar,rangeCalendar=rangeCalendar,platformLogos=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.identity .platform-logo').length===document.querySelectorAll('.account-row').length") == "true",monitorRemoved=await view.CoreWebView2.ExecuteScriptAsync("!document.querySelector('.monitor')") == "true",threeSummaryCards=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.stat-grid article').length===3") == "true",onlyOpenCode=onlyOpenCode,onlyCommandCode=onlyCommandCode,renewalEditor=renewalEditor,renewalSaved=renewalSaved,insideKeeps=insideKeeps,outsideCloses=outsideCloses};
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-platform=\"opencode\"] .identity').click()");await Task.Delay(100);
                string previewDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream openCodePreview=File.Create(Path.Combine(previewDirectory,"opencode-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,openCodePreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.model-view-button').click()");await Task.Delay(100);bool modelStats=await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.model-usage tbody tr').length===1&&!document.querySelector('.history-range')") == "true";
                report["openCodeUI"]=new{addPlatform=openCodeAdd,noClientPanel=noOpenCodePanel,sixTokenFields=openCodeStats,modelStats=modelStats,keyOnly=openCodeKeyOnly};await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.modal-title .icon-button').click()");store.Accounts.Remove(demoOpenCode);Push();await Task.Delay(50);updater.Notes="## 更新内容\n- 改进平台选择\n- 显示更新说明";updater.Latest="1.4.0";updater.Status="available";Push();await Task.Delay(100);
                bool releasePanel=await view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.update-modal')&&document.querySelector('.release-notes').textContent.includes('改进平台选择')&&document.querySelector('.update-modal').getBoundingClientRect().top<85") == "true";
                string updatePreviewDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification");using(FileStream updatePreview=File.Create(Path.Combine(updatePreviewDir,"update-preview.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,updatePreview);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"关闭更新面板\"]').click()");
                bool updateNotice=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.update-prompt').textContent.includes('1.4.0')&&document.querySelector('.update-prompt').getBoundingClientRect().top<30") == "true";
                updater.Status="downloading";updater.Progress=42;Push();await Task.Delay(100);bool updateProgress=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.update-prompt').textContent.includes('42%')&&!document.querySelector('.update-prompt div button')") == "true";
                updater.Status="ready";Push();await Task.Delay(100);bool updateInstall=await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.update-prompt div button').textContent==='安装并重启'") == "true";
                report["updaterUI"]=new{releaseNotes=releasePanel,topNotice=updateNotice,downloadProgress=updateProgress,installAction=updateInstall};updater.Status="idle";Push();await Task.Delay(50);check=new JavaScriptSerializer().Serialize(report);
            }
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification"); Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, liveVerify ? "live-result.json" : "ui-result.json"), check);
            using (FileStream image = File.Create(Path.Combine(output, liveVerify ? "live-preview.png" : "ui-preview.png"))) await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        } catch (Exception error) { string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification"); Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "error.txt"), error.Message); }
        finally { quitting = true; Close(); }
    }
}
}
