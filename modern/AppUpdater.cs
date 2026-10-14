using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
namespace ModernHost {
public sealed class AppUpdater {
    public string Status="idle",Latest="",Error="",Notes="",PublishedAt="";
    public int Progress;
    private string assetUrl,checksumUrl,download,expectedHash;
    private bool working;
    public Action Changed=delegate{};
    public static bool Newer(string candidate,string current) { Version a,b;return Version.TryParse(candidate,out a)&&Version.TryParse(current,out b)&&a>b; }
    public static bool Trusted(string url,string tag,string name) { return url=="https://github.com/xiaoer2021/commandcode-manager/releases/download/v"+tag+"/"+name; }
    public static bool VerifyFile(string path,string expected) { using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").Equals(expected,StringComparison.OrdinalIgnoreCase); }
    private sealed class UpdateClient : WebClient { protected override WebRequest GetWebRequest(Uri address) { var r=base.GetWebRequest(address);r.Timeout=60000;return r; } }
    private WebClient Client() { var c=new UpdateClient();c.Encoding=Encoding.UTF8;c.Headers[HttpRequestHeader.UserAgent]="CommandCode-Manager-Updater";return c; }
    public async Task Check(string current) {
        if(working)return;working=true;Status="checking";Error="";Changed();
        try {
            string json;using(var c=Client())json=await c.DownloadStringTaskAsync("https://api.github.com/repos/xiaoer2021/commandcode-manager/releases/latest");
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);string tag=Convert.ToString(data["tag_name"]);
            if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$")||Convert.ToBoolean(data["draft"])||Convert.ToBoolean(data["prerelease"]))throw new InvalidOperationException("发布版本格式异常");
            Notes=data.ContainsKey("body")?Convert.ToString(data["body"]):"";if(Notes.Length>50000)Notes=Notes.Substring(0,50000);PublishedAt=data.ContainsKey("published_at")?Convert.ToString(data["published_at"]):"";
            Latest=tag.Substring(1);string name="CommandCode-Manager-Setup-"+Latest+".exe";assetUrl=null;checksumUrl=null;
            foreach(Dictionary<string,object> asset in (System.Collections.IEnumerable)data["assets"]) { string n=Convert.ToString(asset["name"]),u=Convert.ToString(asset["browser_download_url"]);if((n==name||n=="SHA256SUMS.txt")&&Trusted(u,Latest,n)){if(n==name){long size=Convert.ToInt64(asset["size"]);if(size<100000||size>268435456)throw new InvalidOperationException("安装包大小异常");assetUrl=u;}else checksumUrl=u;} }
            if(Newer(Latest,current)){if(assetUrl==null||checksumUrl==null)throw new InvalidOperationException("新版本安装文件尚未上传完整，请稍后重试");Status="available";}else Status="current";
        }catch(Exception e){Status="error";Error="检查更新失败："+(e is ArgumentException?"发布信息解析失败，请稍后重试":e.Message);}finally{working=false;Changed();}
    }
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
    private static string Quote(string s){return "'"+s.Replace("'","''")+"'";}
    public void Install() {
        if(Status!="ready"||!File.Exists(download))throw new InvalidOperationException("请先下载更新");
        if(!VerifyFile(download,expectedHash))throw new InvalidOperationException("安装文件已改变，请重新下载更新");
        string exe=Process.GetCurrentProcess().MainModule.FileName,dir=Path.GetDirectoryName(exe);
        if(!File.Exists(Path.Combine(dir,"unins000.exe"))) {Process.Start(new ProcessStartInfo(download){UseShellExecute=true});return;}
        string script="$ErrorActionPreference='Stop'; $targetPid="+Process.GetCurrentProcess().Id+"; $exe="+Quote(exe)+"; $setup="+Quote(download)+"; $expected="+Quote(expectedHash)+"; $ccSetupArgs="+Quote("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG /DIR=\""+dir+"\"")+"; try { Wait-Process -Id $targetPid -Timeout 60 -ErrorAction SilentlyContinue; if(Get-Process -Id $targetPid -ErrorAction SilentlyContinue){throw '程序尚未退出'}; if((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne $expected){throw '安装文件校验失败'}; $p=Start-Process -FilePath $setup -ArgumentList $ccSetupArgs -Wait -PassThru; if($p.ExitCode -ne 0){throw ('安装失败，代码 '+$p.ExitCode)} } catch { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'更新未完成') | Out-Null } finally { Start-Process -FilePath $exe }";
        Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true});
    }
}
}
