using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
namespace ModernHost {
public sealed class AppUpdater {
    public string Status="idle",Latest="",Error="",Notes="",PublishedAt="";
    public int Progress;
    private string assetUrl,checksumUrl,download,expectedHash;
    private bool working;
    public DateTimeOffset LastAttempt=DateTimeOffset.MinValue;
    public bool AutoCheckDue(DateTimeOffset now){return !working&&Status!="ready"&&Status!="downloading"&&(now-LastAttempt).TotalMinutes>=(Status=="error"?5:15);}
    public async Task CheckAutomatically(string current){if(AutoCheckDue(DateTimeOffset.UtcNow))await Check(current);}
    public Action Changed=delegate{};
    public static bool Newer(string candidate,string current) { Version a,b;return Version.TryParse(candidate,out a)&&Version.TryParse(current,out b)&&a>b; }
    public static bool Trusted(string url,string tag,string name) { return url=="https://github.com/xiaoer2021/commandcode-manager/releases/download/v"+tag+"/"+name; }
    public static string LatestTagFromUrl(string url) { Uri uri; if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.Host!="github.com")throw new InvalidOperationException("未能识别 GitHub 最新稳定版本");var m=Regex.Match(uri.AbsolutePath,@"^/xiaoer2021/commandcode-manager/releases/tag/(v\d+\.\d+\.\d+)$");if(!m.Success)throw new InvalidOperationException("未能识别 GitHub 最新稳定版本");return m.Groups[1].Value; }
    public static string NotesFromAtom(string xml,string tag,out string updated) { updated="";var doc=new XmlDocument();doc.XmlResolver=null;doc.LoadXml(xml);var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("a","http://www.w3.org/2005/Atom");foreach(XmlNode entry in doc.SelectNodes("/a:feed/a:entry",ns)){var title=entry.SelectSingleNode("a:title",ns);if(title==null||!Regex.IsMatch(title.InnerText,@"(?:^|\s)"+Regex.Escape(tag)+@"$"))continue;var date=entry.SelectSingleNode("a:updated",ns);updated=date==null?"":date.InnerText;var content=entry.SelectSingleNode("a:content",ns);return content==null?"":AtomHtmlToText(content.InnerText);}throw new InvalidOperationException("GitHub 发布说明中未找到 "+tag); }
    private static string AtomHtmlToText(string value) { string s=System.Net.WebUtility.HtmlDecode(value??"");s=Regex.Replace(s,@"(?i)<br\s*/?>", "\n");s=Regex.Replace(s,@"(?i)</(p|div|h[1-6]|ul|ol|li|pre|blockquote)\s*>","\n");s=Regex.Replace(s,@"(?i)<li\b[^>]*>","- ");s=Regex.Replace(s,@"(?i)<h[1-6]\b[^>]*>","# ");s=Regex.Replace(s,@"<[^>]+>","");s=System.Net.WebUtility.HtmlDecode(s);s=Regex.Replace(s,@"[ \t]+\n","\n");s=Regex.Replace(s,@"\n{3,}","\n\n");return s.Trim(); }
    public static bool VerifyFile(string path,string expected) { using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").Equals(expected,StringComparison.OrdinalIgnoreCase); }
    private sealed class UpdateClient : WebClient { protected override WebRequest GetWebRequest(Uri address) { var r=base.GetWebRequest(address);r.Timeout=15000;return r; } }
    private WebClient Client() { var c=new UpdateClient();c.Encoding=Encoding.UTF8;c.Headers[HttpRequestHeader.UserAgent]="CommandCode-Manager-Updater";return c; }
    private static async Task<T> WithTimeout<T>(Task<T> operation,Action cancel,int timeout,string message) { if(await Task.WhenAny(operation,Task.Delay(timeout)).ConfigureAwait(false)!=operation){cancel();throw new TimeoutException(message);}return await operation.ConfigureAwait(false); }
    private static async Task<HttpWebResponse> ResponseAsync(HttpWebRequest request,string message) { request.Timeout=15000;return (HttpWebResponse)await WithTimeout(request.GetResponseAsync(),()=>request.Abort(),15000,message).ConfigureAwait(false); }
    private static async Task<string> DownloadStringAsync(WebClient client,string url,string message) { return await WithTimeout(client.DownloadStringTaskAsync(url),()=>client.CancelAsync(),15000,message).ConfigureAwait(false); }
    public async Task Check(string current) {
        if(working||Status=="ready")return;working=true;LastAttempt=DateTimeOffset.UtcNow;Status="checking";Error="";Changed();
        try {
            string finalUrl;var request=(HttpWebRequest)WebRequest.Create("https://github.com/xiaoer2021/commandcode-manager/releases/latest");request.UserAgent="MultiAccountWorkspace-Updater";using(var response=await ResponseAsync(request,"连接 GitHub 超时，检查网络或代理后重试"))finalUrl=response.ResponseUri.AbsoluteUri;
            string tag=LatestTagFromUrl(finalUrl),feed;using(var c=Client())feed=await DownloadStringAsync(c,"https://github.com/xiaoer2021/commandcode-manager/releases.atom","读取更新说明超时，检查网络或代理后重试");Notes=NotesFromAtom(feed,tag,out PublishedAt);if(Notes.Length>50000)Notes=Notes.Substring(0,50000);
            Latest=tag.Substring(1);string name="CommandCode-Manager-Setup-"+Latest+".exe";assetUrl=null;checksumUrl=null;
            assetUrl="https://github.com/xiaoer2021/commandcode-manager/releases/download/"+tag+"/"+name;checksumUrl="https://github.com/xiaoer2021/commandcode-manager/releases/download/"+tag+"/SHA256SUMS.txt";
            var installerCheck=AssetAvailableAsync(assetUrl);var checksumCheck=AssetAvailableAsync(checksumUrl);await Task.WhenAll(installerCheck,checksumCheck).ConfigureAwait(false);if(!installerCheck.Result||!checksumCheck.Result)throw new InvalidOperationException("GitHub 上的新版本安装文件暂不可访问（可能返回 403 或尚未同步），请稍后重试");
            if(Newer(Latest,current)){if(assetUrl==null||checksumUrl==null)throw new InvalidOperationException("新版本安装文件尚未上传完整，请稍后重试");Status="available";}else Status="current";
        }catch(Exception e){Status="error";Error="检查更新失败："+(e is ArgumentException?"发布信息解析失败，请稍后重试":e.Message);}finally{working=false;Changed();}
    }
    private static async Task<bool> AssetAvailableAsync(string url) { var request=(HttpWebRequest)WebRequest.Create(url);request.Method="HEAD";request.UserAgent="MultiAccountWorkspace-Updater";request.AllowAutoRedirect=true;try{using(var response=await ResponseAsync(request,"检查更新文件超时，检查网络或代理后重试"))return response.StatusCode==HttpStatusCode.OK;}catch(WebException e){var response=e.Response as HttpWebResponse;if(response!=null){HttpStatusCode status=response.StatusCode;response.Dispose();if(status==HttpStatusCode.Forbidden)throw new InvalidOperationException("GitHub 拒绝访问更新文件（HTTP 403），请检查网络代理后重试");}return false;} }
    public async Task Download() {
        if(working||Status!="available")return;working=true;Status="downloading";Progress=0;Error="";Changed();
        try {
            string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CommandCodeManager","updates");Directory.CreateDirectory(dir);string name="CommandCode-Manager-Setup-"+Latest+".exe";download=Path.Combine(dir,name);
            string sums;using(var c=Client()){sums=await c.DownloadStringTaskAsync(checksumUrl);c.DownloadProgressChanged+=(s,e)=>{Progress=e.ProgressPercentage;Changed();};await c.DownloadFileTaskAsync(assetUrl,download+".partial");}
            string line=sums.Split('\n').FirstOrDefault(l=>Regex.IsMatch(l.Trim(),@"^[a-fA-F0-9]{64}\s+\*?"+Regex.Escape(name)+"$"));if(line==null)throw new InvalidOperationException("安装包校验信息缺失");
            expectedHash=line.Substring(0,64);if(!VerifyFile(download+".partial",expectedHash))throw new InvalidOperationException("安装包校验失败，请重新下载");
            if(File.Exists(download))File.Delete(download);File.Move(download+".partial",download);Status="ready";
        }catch(Exception e){Status="available";Error="下载失败："+e.Message;}finally{working=false;Changed();}
    }
    public static string HashCheckScript() { return "$sha=[Security.Cryptography.SHA256]::Create(); $stream=$null; try {$stream=[IO.File]::OpenRead($setup); $actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-',''); if(-not [string]::Equals($actual,$expected,[StringComparison]::OrdinalIgnoreCase)){throw '安装文件校验失败'}} finally {if($null -ne $stream){$stream.Dispose()}; $sha.Dispose()}"; }
    private static string Quote(string s){return "'"+s.Replace("'","''")+"'";}
    public void Install() {
        if(Status!="ready"||!File.Exists(download))throw new InvalidOperationException("请先下载更新");
        if(!VerifyFile(download,expectedHash))throw new InvalidOperationException("安装文件已改变，请重新下载更新");
        string exe=Process.GetCurrentProcess().MainModule.FileName,dir=Path.GetDirectoryName(exe);
        if(!File.Exists(Path.Combine(dir,"unins000.exe"))) {Process.Start(new ProcessStartInfo(download){UseShellExecute=true});return;}
        string script="$ErrorActionPreference='Stop'; $targetPid="+Process.GetCurrentProcess().Id+"; $exe="+Quote(exe)+"; $setup="+Quote(download)+"; $expected="+Quote(expectedHash)+"; $ccSetupArgs="+Quote("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG /DIR=\""+dir+"\"")+"; try { $target=$null; try {$target=[Diagnostics.Process]::GetProcessById($targetPid)} catch [ArgumentException] {} if($null -ne $target){try {if(-not $target.WaitForExit(60000)){throw '程序尚未退出'}} finally {$target.Dispose()}}; "+HashCheckScript()+"; $info=New-Object Diagnostics.ProcessStartInfo; $info.FileName=$setup; $info.Arguments=$ccSetupArgs; $info.UseShellExecute=$false; $p=[Diagnostics.Process]::Start($info); try {$p.WaitForExit(); if($p.ExitCode -ne 0){throw ('安装失败，代码 '+$p.ExitCode)}} finally {$p.Dispose()} } catch { Add-Type -AssemblyName System.Windows.Forms; [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'更新未完成') } finally { $namedExe=[IO.Path]::Combine([IO.Path]::GetDirectoryName($exe),'多账号工作台.exe'); if([IO.File]::Exists($namedExe)){$exe=$namedExe}; [void][Diagnostics.Process]::Start($exe) }";
        string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        Process.Start(new ProcessStartInfo(shell,"-NoProfile -ExecutionPolicy Bypass -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true});
    }
}
}
