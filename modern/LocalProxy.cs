using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CommandCodeManager;
namespace ModernHost {
public sealed class ProxyAccount {
    public string Id,Name,Key,CredentialState,Provider="commandcode";
    public bool ApiSupported=true,QuotaKnown;
    public double? Balance,Headroom;
    public DateTimeOffset? BlockedUntil;
    public string BlockReason;
    public DateTimeOffset? QuotaUntil;
}
public sealed class ProxyRecord {
    public string At,Account,Model,Endpoint,Note;
    public int Status,DurationMs;
    public double? Input,Output;
}
public sealed class LocalProxy : IDisposable {
    private sealed class Catalog {public DateTimeOffset At;public Dictionary<string,string[]> Models;}
    private sealed class Request {public string Method,Path;public Dictionary<string,string> Headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);public byte[] Body;}
    private sealed class ProtocolFailure : Exception {public int Status;public ProtocolFailure(int status,string text):base(text){Status=status;}}
    private readonly object gate=new object();
    private readonly Func<ProxyAccount[]> snapshot;
    private readonly HttpClient upstream;
    private readonly SemaphoreSlim slots=new SemaphoreSlim(8,8);
    private readonly List<ProxyRecord> records=new List<ProxyRecord>();
    private readonly Dictionary<string,DateTimeOffset> cooldown=new Dictionary<string,DateTimeOffset>();
    private readonly Dictionary<string,string> pauseReasons=new Dictionary<string,string>();
    private readonly Dictionary<string,int> busy=new Dictionary<string,int>();
    private readonly Dictionary<string,Catalog> catalogs=new Dictionary<string,Catalog>();
    private readonly HashSet<TcpClient> connections=new HashSet<TcpClient>();
    private TcpListener listener;
    private CancellationTokenSource lifetime;
    private string secret;
    private string[] accessKeys;
    private string[] pool=new string[0];
    private int port=17766,total,inFlight;
    private string bindAddress="127.0.0.1";
    private bool disposed;
    public string Error="";
    public bool RequireLan,RequireVerifiedAccounts;
    public Action Changed=delegate{};
    private static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=8388608,RecursionLimit=100};}
    public LocalProxy(Func<ProxyAccount[]> accounts,HttpMessageHandler handler=null){snapshot=accounts;upstream=handler==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}):new HttpClient(handler);upstream.Timeout=TimeSpan.FromMinutes(5);upstream.DefaultRequestHeaders.UserAgent.ParseAdd("MultiAccountWorkspace-LocalProxy/1.0");}
    public static string GenerateKey(){byte[] bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return "mw_"+Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');}
    public bool Running {get{lock(gate)return listener!=null;}}
    public int InFlight {get{return Volatile.Read(ref inFlight);}}
    public void SetAccessKeys(IEnumerable<string> values){lock(gate)accessKeys=values.Where(v=>!string.IsNullOrEmpty(v)).Distinct().ToArray();Changed();}
    public void SetPool(IEnumerable<string> ids){lock(gate)pool=ids.Distinct().ToArray();Changed();}
    public bool CanJoin(string id){var account=snapshot().FirstOrDefault(a=>a.Id==id);if(account==null||!account.ApiSupported||account.CredentialState!="valid"||!account.QuotaKnown||string.IsNullOrEmpty(account.Key)||account.BlockedUntil>DateTimeOffset.UtcNow)return false;lock(gate){DateTimeOffset until;return !cooldown.TryGetValue(id,out until)||until<=DateTimeOffset.UtcNow;}}
    public string[] JoinableIds(){var accounts=snapshot().Where(a=>a.ApiSupported&&a.CredentialState=="valid"&&a.QuotaKnown&&!string.IsNullOrEmpty(a.Key)&&(!a.BlockedUntil.HasValue||a.BlockedUntil<=DateTimeOffset.UtcNow)).ToArray();lock(gate)return accounts.Where(a=>!cooldown.ContainsKey(a.Id)||cooldown[a.Id]<=DateTimeOffset.UtcNow).Select(a=>a.Id).ToArray();}
    public static string DefaultLanAddress(){var item=NetworkAddresses().Where(a=>a.lan).OrderByDescending(a=>a.gateway).FirstOrDefault();return item==null?null:item.address;}
    public sealed class NetworkAddress {public string address,label;public bool lan,gateway;}
    private static readonly object networkGate=new object();private static NetworkAddress[] networkCache;private static DateTimeOffset networkUntil;
    public static NetworkAddress[] NetworkAddresses(bool refresh=false){lock(networkGate){if(!refresh&&networkCache!=null&&DateTimeOffset.UtcNow<networkUntil)return networkCache;networkCache=ScanNetworkAddresses();networkUntil=DateTimeOffset.UtcNow.AddMinutes(1);return networkCache;}}
    private static NetworkAddress[] ScanNetworkAddresses(){var addresses=new List<NetworkAddress>{new NetworkAddress{address="127.0.0.1",label="本机 · 127.0.0.1"}};try{foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a=>a.OperationalStatus==OperationalStatus.Up&&a.NetworkInterfaceType!=NetworkInterfaceType.Loopback)){foreach(var unicast in adapter.GetIPProperties().UnicastAddresses){IPAddress ip=unicast.Address;if(ip.AddressFamily!=AddressFamily.InterNetwork||IPAddress.IsLoopback(ip)||ip.ToString().StartsWith("169.254."))continue;if(!addresses.Any(a=>a.address==ip.ToString()))addresses.Add(new NetworkAddress{address=ip.ToString(),label=adapter.Name+" · "+ip,lan=(ip.GetAddressBytes()[0]==10||(ip.GetAddressBytes()[0]==172&&ip.GetAddressBytes()[1]>=16&&ip.GetAddressBytes()[1]<=31)||(ip.GetAddressBytes()[0]==192&&ip.GetAddressBytes()[1]==168))&&(adapter.NetworkInterfaceType==NetworkInterfaceType.Ethernet||adapter.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)&&!System.Text.RegularExpressions.Regex.IsMatch(adapter.Name+" "+adapter.Description,"VPN|Wintun|WireGuard|TAP|TUN|Tailscale|ZeroTier|Hyper-V|VirtualBox|VMware",System.Text.RegularExpressions.RegexOptions.IgnoreCase),gateway=adapter.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any))});}}}catch(NetworkInformationException){}return addresses.ToArray();}
    public object State(){var accounts=snapshot();lock(gate)return new{accountStates=accounts.Select(a=>{DateTimeOffset paused;cooldown.TryGetValue(a.Id,out paused);bool exhausted=a.BlockedUntil.HasValue&&a.BlockedUntil>DateTimeOffset.UtcNow;string reason;pauseReasons.TryGetValue(a.Id,out reason);string status=!a.ApiSupported||string.IsNullOrEmpty(a.Key)?"unsupported":(a.CredentialState=="invalid"||a.CredentialState=="forbidden")?"invalid":a.CredentialState!="valid"?"unknown":exhausted?(a.BlockReason!=null&&a.BlockReason.Contains("安全阈值")?"low_quota":"exhausted"):paused>DateTimeOffset.UtcNow?"paused":!a.QuotaKnown?"unknown":"ready";return new{id=a.Id,status=status,reason=(status=="exhausted"||status=="low_quota")?a.BlockReason:status=="paused"?reason:status=="invalid"?(a.CredentialState=="forbidden"?"访问权限不足":"凭据已失效"):status=="unknown"?"等待额度或凭据检测":status=="unsupported"?"此账号不支持 API":"",resumeAt=status=="paused"?paused.ToString("o"):(status=="exhausted"||status=="low_quota")?a.BlockedUntil.Value.ToString("o"):null};}).ToArray(),running=listener!=null,port=port,bindAddress=bindAddress,networkAddresses=NetworkAddresses(),baseUrl=RequireLan&&bindAddress=="127.0.0.1"?"":"http://"+bindAddress+":"+port+"/v1",poolIds=pool.ToArray(),requests=total,inFlight=InFlight,error=Error,logs=records.AsEnumerable().Reverse().Take(40).Select(r=>new{at=r.At,account=r.Account,model=r.Model,endpoint=r.Endpoint,status=r.Status,durationMs=r.DurationMs,input=r.Input,output=r.Output,note=r.Note}).ToArray(),cooling=cooldown.Count(p=>p.Value>DateTimeOffset.UtcNow)};}
    public void Configure(int number,string key,IEnumerable<string> ids,bool enabled,string address="127.0.0.1"){IPAddress binding;if(!IPAddress.TryParse(address,out binding)||binding.AddressFamily!=AddressFamily.InterNetwork||(enabled&&!NetworkAddresses(true).Any(a=>a.address==address)&&!(Running&&address==bindAddress&&number==port)))throw new InvalidOperationException("请选择当前可用的本机或网络地址");if(disposed)throw new ObjectDisposedException("LocalProxy");if(number<1024||number>65535)throw new InvalidOperationException("端口需在 1024–65535 之间");string[] selected=ids.Distinct().ToArray();if(enabled&&(string.IsNullOrEmpty(key)))throw new InvalidOperationException("请先选择账号并生成代理 Key");lock(gate){foreach(string id in cooldown.Keys.Where(id=>!selected.Contains(id)).ToArray())cooldown.Remove(id);foreach(string id in catalogs.Keys.Where(id=>!selected.Contains(id)).ToArray())catalogs.Remove(id);if(listener!=null&&number==port&&address==bindAddress){secret=key;pool=selected;if(!enabled)StopLocked();Error="";return;}if(InFlight>0)throw new InvalidOperationException("请求正在进行，请完成后再修改端口");TcpListener next=null;if(enabled){next=new TcpListener(binding,number);next.Server.ExclusiveAddressUse=true;try{next.Start(32);}catch(SocketException){throw new InvalidOperationException("该端口无法使用或已被占用，请换一个端口");}}StopLocked();port=number;bindAddress=address;secret=key;pool=selected;Error="";if(next!=null){listener=next;lifetime=new CancellationTokenSource();Task ignored=Accept(next,lifetime.Token);}}Changed();}
    private void StopLocked(){if(listener!=null){listener.Stop();listener=null;}if(lifetime!=null){lifetime.Cancel();lifetime.Dispose();lifetime=null;}foreach(var client in connections.ToArray())client.Close();connections.Clear();}
    public void ClearCooldown(){lock(gate){cooldown.Clear();pauseReasons.Clear();catalogs.Clear();}Changed();}
    public void RecoverAccount(string id){lock(gate){string reason;pauseReasons.TryGetValue(id,out reason);if(reason!="达到请求速率限制"){cooldown.Remove(id);pauseReasons.Remove(id);}catalogs.Remove(id);}Changed();}
    public static void ApplyQuota(ProxyAccount account,AccountData data,DateTimeOffset now,double reservePercent=5){
        if(data==null)return;DateTimeOffset fetched;if(!Core.TryTime(data.FetchedAt,out fetched)||fetched>now.AddMinutes(1)||now-fetched>TimeSpan.FromMinutes(10))return;
        reservePercent=Math.Max(0,Math.Min(20,reservePercent));DateTimeOffset freshUntil=fetched.AddMinutes(10);var windows=new List<Tuple<double?,double?,string,string>>();
        if(data.Limited==true){if(data.FiveHour!=null)windows.Add(Tuple.Create(data.FiveHour.Used,data.FiveHour.Cap,data.FiveHour.ResetAt,"5 小时"));if(data.Weekly!=null)windows.Add(Tuple.Create(data.Weekly.Used,data.Weekly.Cap,data.Weekly.ResetAt,"每周"));}
        double monthlyLeft=data.MonthlyTotal.HasValue&&data.MonthlyUsed.HasValue?Math.Max(0,data.MonthlyTotal.Value-data.MonthlyUsed.Value):0;
        bool additional=data.Purchased.GetValueOrDefault()+data.Free.GetValueOrDefault()>0.000001||(data.Remaining.HasValue&&data.MonthlyTotal.HasValue&&data.MonthlyUsed.HasValue&&data.Remaining.Value-monthlyLeft>0.000001);
        if(account.Provider!="commandcode"||!additional)windows.Add(Tuple.Create(data.MonthlyUsed,data.MonthlyTotal,string.IsNullOrEmpty(data.MonthlyResetAt)?data.PeriodEnd:data.MonthlyResetAt,"月度"));
        foreach(var item in windows){DateTimeOffset reset;bool hasReset=Core.TryTime(item.Item3,out reset);if(hasReset&&reset<=now)continue;if(!item.Item1.HasValue||!item.Item2.HasValue||item.Item2<=0)continue;account.QuotaKnown=true;double remaining=Math.Max(0,100-item.Item1.Value/item.Item2.Value*100);account.Headroom=Math.Min(account.Headroom??100,remaining);if(remaining>reservePercent&&remaining>0)continue;DateTimeOffset until=hasReset&&reset<freshUntil?reset:freshUntil;string reason=remaining<=0?item.Item4+"额度用尽":item.Item4+"余量低于安全阈值（"+reservePercent.ToString("0.#")+"%）";if(!account.BlockedUntil.HasValue||remaining<=0||account.BlockReason==null||account.BlockReason.Contains("安全阈值")){account.BlockedUntil=until;account.BlockReason=reason;}}
        DateTimeOffset creditReset;bool resetPassed=data.MonthlyTotal.HasValue&&data.MonthlyTotal>0&&Core.TryTime(string.IsNullOrEmpty(data.MonthlyResetAt)?data.PeriodEnd:data.MonthlyResetAt,out creditReset)&&creditReset<=now;
        if(data.Remaining.HasValue&&!resetPassed){account.QuotaKnown=true;if(data.Remaining<=0&&account.Provider=="commandcode"){account.BlockedUntil=freshUntil;account.BlockReason="可用余额为零";}}
    }
    public void Stop(){lock(gate)StopLocked();Changed();}
    private async Task Accept(TcpListener server,CancellationToken token){while(!token.IsCancellationRequested){TcpClient client;try{client=await server.AcceptTcpClientAsync();}catch(ObjectDisposedException){break;}catch(SocketException){break;}if(!slots.Wait(0)){try{await Failure(client.GetStream(),429,"local_busy","本地并发已满，请稍后重试");}catch{}client.Close();continue;}lock(gate)connections.Add(client);Task ignored=Serve(client,token);}}
    private static async Task<string> Line(Stream stream,int maximum,CancellationToken token){var bytes=new List<byte>();byte[] one=new byte[1];while(true){if(await stream.ReadAsync(one,0,1,token)==0)throw new ProtocolFailure(400,"请求提前结束");if(one[0]==10){if(bytes.Count==0||bytes[bytes.Count-1]!=13)throw new ProtocolFailure(400,"请求格式不正确");bytes.RemoveAt(bytes.Count-1);return Encoding.ASCII.GetString(bytes.ToArray());}if(bytes.Count>=maximum)throw new ProtocolFailure(413,"请求头或分块过长");bytes.Add(one[0]);}}
    private static async Task<byte[]> Bytes(Stream stream,int count,CancellationToken token){byte[] bytes=new byte[count];int offset=0;while(offset<count){int got=await stream.ReadAsync(bytes,offset,count-offset,token);if(got==0)throw new ProtocolFailure(400,"请求内容提前结束");offset+=got;}return bytes;}
    private static async Task<Request> Read(Stream stream,CancellationToken token){string[] start=(await Line(stream,4096,token)).Split(' ');if(start.Length!=3||start[2]!="HTTP/1.1"||!start[1].StartsWith("/")||start[1].Contains("#"))throw new ProtocolFailure(400,"仅接受标准本地 HTTP 请求");var request=new Request{Method=start[0],Path=start[1]};int size=0;while(true){string line=await Line(stream,8192,token);if(line=="")break;size+=line.Length;if(size>16384)throw new ProtocolFailure(413,"请求头过大");int split=line.IndexOf(':');if(split<=0||line[0]==' '||line[0]=='\t')throw new ProtocolFailure(400,"请求头格式异常");string name=line.Substring(0,split),value=line.Substring(split+1).Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^[A-Za-z0-9-]+$")||value.Any(c=>c<32&&c!='\t')||request.Headers.ContainsKey(name))throw new ProtocolFailure(400,"重复或无效的请求头");request.Headers.Add(name,value);}string expectation;if(request.Headers.TryGetValue("Expect",out expectation)){if(!string.Equals(expectation,"100-continue",StringComparison.OrdinalIgnoreCase))throw new ProtocolFailure(417,"不支持此 Expect 请求头");byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");await stream.WriteAsync(interim,0,interim.Length,token);}string transfer,length;bool chunked=request.Headers.TryGetValue("Transfer-Encoding",out transfer),sized=request.Headers.TryGetValue("Content-Length",out length);if(chunked&&sized)throw new ProtocolFailure(400,"请求长度冲突");using(var content=new MemoryStream()){if(chunked){if(!string.Equals(transfer,"chunked",StringComparison.OrdinalIgnoreCase))throw new ProtocolFailure(400,"不支持此传输方式");while(true){string line=await Line(stream,128,token);int count;if(!int.TryParse(line.Split(';')[0],System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out count)||count<0||count>8388608-content.Length)throw new ProtocolFailure(413,"请求内容最多 8 MB");if(count==0){if(await Line(stream,8192,token)!="")throw new ProtocolFailure(400,"不接受附加请求尾部");break;}byte[] part=await Bytes(stream,count,token);content.Write(part,0,part.Length);if(await Line(stream,2,token)!="")throw new ProtocolFailure(400,"分块格式异常");}}else if(sized){int count;if(!int.TryParse(length,out count)||count<0||count>8388608)throw new ProtocolFailure(413,"请求内容最多 8 MB");byte[] part=await Bytes(stream,count,token);content.Write(part,0,part.Length);}request.Body=content.ToArray();}return request;}
    private static bool Equal(string a,string b){if(a==null||b==null)return false;byte[] x=Encoding.UTF8.GetBytes(a),y=Encoding.UTF8.GetBytes(b);int result=x.Length^y.Length;for(int i=0;i<Math.Max(x.Length,y.Length);i++)result|=(i<x.Length?x[i]:0)^(i<y.Length?y[i]:0);return result==0;}
    private static async Task Headers(Stream output,int status,string type,HttpResponseMessage response=null){var headers=new StringBuilder("HTTP/1.1 "+status+" "+(status>=400?"Error":"OK")+"\r\nContent-Type: "+type+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Accel-Buffering: no\r\nx-should-retry: false\r\n");if(response!=null){var allowed=new[]{"anthropic-","openai-","x-goog-","x-ratelimit-","ratelimit-","x-request-id","request-id","retry-after","www-authenticate","vary"};foreach(var item in response.Headers.Concat(response.Content.Headers)){string name=item.Key;if(name.Equals("Content-Type",StringComparison.OrdinalIgnoreCase)||name.Equals("Content-Length",StringComparison.OrdinalIgnoreCase)||name.Equals("Connection",StringComparison.OrdinalIgnoreCase)||name.Equals("Transfer-Encoding",StringComparison.OrdinalIgnoreCase)||!(allowed.Any(prefix=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))))continue;foreach(string value in item.Value)if(!value.Contains("\r")&&!value.Contains("\n"))headers.Append(name).Append(": ").Append(value).Append("\r\n");}}headers.Append("\r\n");byte[] bytes=Encoding.UTF8.GetBytes(headers.ToString());await output.WriteAsync(bytes,0,bytes.Length);}
    private static async Task Failure(Stream output,int status,string code,string message){await Headers(output,status,"application/json; charset=utf-8");byte[] bytes=Encoding.UTF8.GetBytes(Json().Serialize(new{error=new{type=code,code=code,message=message}}));await output.WriteAsync(bytes,0,bytes.Length);}
    private static async Task SafeFailure(TcpClient client,int status,string code,string message){try{await Failure(client.GetStream(),status,code,message);}catch{}}
    private static string NormalizeApiPath(string target){if(string.IsNullOrEmpty(target)||target.Length>2048||target[0]!='/'||target.Contains('#')||target.Contains('\\')||target.Any(c=>char.IsControl(c)))return null;int query=target.IndexOf('?');string path=query<0?target:target.Substring(0,query);if(path.Contains("//")||path.Split('/').Any(s=>s=="."||s=="..")||path.IndexOf("%2f",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("%5c",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("%2e",StringComparison.OrdinalIgnoreCase)>=0)return null;if(path.StartsWith("/v1/v1/",StringComparison.Ordinal))path=path.Substring(3);if(path=="/messages"||path=="/responses"||path=="/chat/completions"||path=="/models")path="/v1"+path;return path.StartsWith("/v1/",StringComparison.Ordinal)?path:null;}
    private static string QueryPart(string target){int query=target.IndexOf('?');return query<0?"":target.Substring(query);}
    private static bool IsJson(Request request){string value;return request.Headers.TryGetValue("Content-Type",out value)&&value.IndexOf("json",StringComparison.OrdinalIgnoreCase)>=0;}
    private static string RequestModel(Request request,string target){if(IsJson(request)&&request.Body.Length>0)try{return Core.Text(Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(request.Body)),"model");}catch{}string contentType;if(request.Headers.TryGetValue("Content-Type",out contentType)&&request.Body.Length>0){string form=Encoding.GetEncoding(28591).GetString(request.Body);if(contentType.IndexOf("multipart/form-data",StringComparison.OrdinalIgnoreCase)>=0){var match=System.Text.RegularExpressions.Regex.Match(form,"name=\"model\"\\r\\n(?:Content-Type: [^\\r\\n]*\\r\\n)?\\r\\n([^\\r\\n]+)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(match.Success)return match.Groups[1].Value;}else if(contentType.IndexOf("application/x-www-form-urlencoded",StringComparison.OrdinalIgnoreCase)>=0){string value=FormValue(form,"model");if(value!="")return value;}}string query=QueryPart(target);return FormValue(query.TrimStart('?'),"model");}
    private static string FormValue(string form,string name){foreach(string part in form.Split('&')){string[] pair=part.Split(new[]{'='},2);if(pair.Length==2&&Uri.UnescapeDataString(pair[0].Replace('+',' '))==name)return Uri.UnescapeDataString(pair[1].Replace('+',' '));}return "";}
    private async Task Serve(TcpClient client,CancellationToken stop){bool started=false;Task failureReply=null;using(client)using(var token=CancellationTokenSource.CreateLinkedTokenSource(stop))using(var readTimer=new Timer(_=>{try{client.Close();}catch{}},null,20000,Timeout.Infinite))using(var clientWatch=new Timer(_=>{try{var socket=client.Client;if(socket!=null&&socket.Poll(0,SelectMode.SelectRead)&&socket.Available==0){token.Cancel();client.Close();}}catch(ObjectDisposedException){}catch(SocketException){}},null,Timeout.Infinite,Timeout.Infinite)){try{client.NoDelay=true;Stream output=client.GetStream();Request request=await Read(output,token.Token);readTimer.Change(Timeout.Infinite,Timeout.Infinite);clientWatch.Change(500,500);token.CancelAfter(TimeSpan.FromMinutes(5));string[] expected;lock(gate)expected=accessKeys??new[]{secret};string auth,provided=null;if(request.Headers.TryGetValue("Authorization",out auth)&&auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))provided=auth.Substring(7).Trim();if(provided==null)request.Headers.TryGetValue("x-api-key",out provided);bool authorized=false;foreach(string accepted in expected)authorized|=Equal(provided,accepted);if(!authorized)throw new ProtocolFailure(401,"代理 Key 不正确，请使用工作台生成的 Key");if(request.Headers.ContainsKey("Origin"))throw new ProtocolFailure(403,"本地代理暂不允许网页跨域请求，请通过软件后端调用");string host;if(!request.Headers.TryGetValue("Host",out host)||!(host==((IPEndPoint)client.Client.LocalEndPoint).Address.ToString()+":"+port||(IPAddress.IsLoopback(((IPEndPoint)client.Client.LocalEndPoint).Address)&&host=="localhost:"+port)))throw new ProtocolFailure(403,"请求地址与所选监听地址不一致");string target=request.Path;string path=NormalizeApiPath(target);if(request.Method=="GET"&&target.Split('?')[0]=="/health"){await Headers(output,200,"application/json");byte[] bytes=Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");await output.WriteAsync(bytes,0,bytes.Length);return;}if(request.Method=="GET"&&path=="/v1/models"){await Models(output,token.Token);return;}if(path==null||!(request.Method=="POST"||request.Method=="GET"||request.Method=="PUT"||request.Method=="PATCH"||request.Method=="DELETE"))throw new ProtocolFailure(404,"仅支持账号模型列表中公布的 /v1/ 接口路径和 GET、POST、PUT、PATCH、DELETE 方法");string model=RequestModel(request,target);if(string.IsNullOrWhiteSpace(model)&&path.StartsWith("/v1/models/",StringComparison.Ordinal))model=Uri.UnescapeDataString(path.Substring("/v1/models/".Length));if(model.Length>256)throw new ProtocolFailure(400,"请求必须在 JSON 内容或查询参数中提供模型 ID");if(request.Method=="POST"&&path=="/v1/chat/completions"&&IsJson(request)){var body=Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(request.Body));if(Core.Boolean(body,"stream")==true){var options=Core.Object(Core.Value(body,"stream_options"));options["include_usage"]=true;body["stream_options"]=options;request.Body=Encoding.UTF8.GetBytes(Json().Serialize(body));}}Interlocked.Increment(ref total);Interlocked.Increment(ref inFlight);Changed();try{await Forward(output,request,path+QueryPart(target),model,()=>started=true,token.Token);}finally{Interlocked.Decrement(ref inFlight);Changed();}}catch(ProtocolFailure e){if(!started)failureReply=SafeFailure(client,e.Status,"local_proxy_error",e.Message);}catch(Exception){if(!started)failureReply=SafeFailure(client,502,"upstream_unavailable","上游连接未完成，本次未自动重试；请检查请求记录后再试");}finally{lock(gate)connections.Remove(client);slots.Release();}if(failureReply!=null)await failureReply;}}
    private ProxyAccount[] Accounts(){string[] selected;lock(gate)selected=pool.ToArray();var accounts=snapshot().Where(a=>selected.Contains(a.Id)&&a.ApiSupported&&a.CredentialState!="invalid"&&!string.IsNullOrEmpty(a.Key)&&(!RequireVerifiedAccounts||(a.QuotaKnown&&a.CredentialState=="valid"))&&(!a.BlockedUntil.HasValue||a.BlockedUntil<=DateTimeOffset.UtcNow)).ToArray();lock(gate)return accounts.Where(a=>!cooldown.ContainsKey(a.Id)||cooldown[a.Id]<=DateTimeOffset.UtcNow).OrderBy(a=>busy.ContainsKey(a.Id)?busy[a.Id]:0).ThenByDescending(a=>a.Headroom??-1).ThenByDescending(a=>a.Balance??0).ToArray();}
    private static string UpstreamBase(ProxyAccount account){if(account.Provider=="commandcode")return "https://api.commandcode.ai/provider/v1";if(account.Provider=="opencode-go")return "https://opencode.ai/inference/go/v1";if(account.Provider=="opencode-zen")return "https://opencode.ai/inference/v1";throw new InvalidOperationException("不支持的代理平台");}
    private async Task<Catalog> ModelsFor(ProxyAccount account,CancellationToken token){lock(gate){Catalog old;if(catalogs.TryGetValue(account.Id,out old)&&(DateTimeOffset.UtcNow-old.At).TotalMinutes<5)return old;}using(var request=new HttpRequestMessage(HttpMethod.Get,UpstreamBase(account)+"/models")){request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",account.Key);using(var response=await upstream.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token))using(var cancel=token.Register(()=>response.Dispose())){if(!response.IsSuccessStatusCode){if((int)response.StatusCode==401||response.StatusCode==(HttpStatusCode)429)Cool(account.Id,DateTimeOffset.UtcNow.AddMinutes(1));throw new ProtocolFailure(503,"此账号暂时无法拉取模型，请刷新账号状态或稍后重试");}byte[] bytes=await Limited(await response.Content.ReadAsStreamAsync(),1048576,token);var root=Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));var items=Core.Value(root,"data") as System.Collections.IEnumerable;if(items==null)throw new ProtocolFailure(502,"官方模型列表格式异常");var catalog=new Catalog{At=DateTimeOffset.UtcNow,Models=new Dictionary<string,string[]>(StringComparer.Ordinal)};foreach(object item in items){var data=Core.Object(item);string id=Core.Text(data,"id");if(string.IsNullOrWhiteSpace(id)||id.Length>256)continue;var endpoints=Core.Value(data,"supported_endpoints") as System.Collections.IEnumerable;if(account.Provider!="commandcode"){string route=OpenCodeProxyModels.Endpoint(account.Provider,id);if(route==null)continue;catalog.Models[account.Provider+"/"+id]=new[]{route};}else catalog.Models[id]=endpoints==null?new string[0]:endpoints.Cast<object>().Select(Convert.ToString).ToArray();}lock(gate)catalogs[account.Id]=catalog;return catalog;}}}
    private async Task Models(Stream output,CancellationToken token){var combined=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);var candidates=Accounts();if(candidates.Length==0)throw new ProtocolFailure(503,"账号池没有可用账号，请选择账号或等待冷却结束");int successful=0;foreach(var account in candidates){try{var models=await ModelsFor(account,token);successful++;foreach(var item in models.Models){HashSet<string> set;if(!combined.TryGetValue(item.Key,out set)){set=new HashSet<string>();combined[item.Key]=set;}foreach(string endpoint in item.Value)set.Add(endpoint);}}catch(ProtocolFailure){}}if(successful==0)throw new ProtocolFailure(503,"账号池暂时无法获取模型，请刷新账号状态");await Headers(output,200,"application/json; charset=utf-8");byte[] bytes=Encoding.UTF8.GetBytes(Json().Serialize(new{@object="list",data=combined.Select(p=>new{id=p.Key,@object="model",owned_by=p.Key.StartsWith("opencode")?"opencode":"commandcode",supported_endpoints=p.Value.ToArray()}).ToArray()})).ToArray();await output.WriteAsync(bytes,0,bytes.Length);}
    private static bool Supports(Catalog catalog,string model,string path){return catalog.Models.ContainsKey(model);}
    private void Cool(string id,DateTimeOffset until,string reason="上游暂时拒绝请求"){lock(gate){cooldown[id]=until;pauseReasons[id]=reason;}}
    private void Reserve(string id,int delta){lock(gate){int count;busy.TryGetValue(id,out count);busy[id]=Math.Max(0,count+delta);}}
    private void Log(ProxyRecord record){lock(gate){records.Add(record);if(records.Count>40)records.RemoveAt(0);}Changed();}
    private async Task Forward(Stream output,Request incoming,string path,string model,Action committed,CancellationToken token){
        ProxyAccount[] candidates=Accounts();
        if(candidates.Length==0)throw new ProtocolFailure(503,"没有可用账号，请检查账号池或等待冷却结束");
        bool compatible=false;
        int last=503;
        foreach(var account in candidates){
            Catalog catalog;
            try{catalog=await ModelsFor(account,token);}
            catch(ProtocolFailure){Log(new ProxyRecord{At=DateTimeOffset.UtcNow.ToString("o"),Account=account.Name,Model=model,Endpoint=path,Status=503,Note="模型列表不可用，尝试下一账号"});continue;}
            if(!Supports(catalog,model,path))continue;
            compatible=true;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var record=new ProxyRecord{At=DateTimeOffset.UtcNow.ToString("o"),Account=account.Name,Model=model,Endpoint=path.Split('?')[0]};
            Reserve(account.Id,1);
            bool began=false;
            try{
                string target=path.Split('?')[0].Substring(3)+QueryPart(path);
                using(var request=new HttpRequestMessage(new HttpMethod(incoming.Method),UpstreamBase(account)+target)){
                    request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",account.Key);
                    request.Headers.UserAgent.ParseAdd("MultiAccountWorkspace-LocalProxy/1.0");
                    var blockedHeaders=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Authorization","x-api-key","Host","Origin","Connection","Keep-Alive","Proxy-Authenticate","Proxy-Authorization","TE","Trailer","Transfer-Encoding","Upgrade","Content-Length","Content-Type","Expect"};
                    foreach(var header in incoming.Headers){
                        if(!blockedHeaders.Contains(header.Key))request.Headers.TryAddWithoutValidation(header.Key,header.Value);
                    }
                    byte[] body=incoming.Body;
                    if(account.Provider!="commandcode"&&IsJson(incoming)&&body.Length>0){
                        var payload=Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(body));
                        if(payload.ContainsKey("model"))payload["model"]=model.Substring(account.Provider.Length+1);
                        body=Encoding.UTF8.GetBytes(Json().Serialize(payload));
                    }
                    if(body.Length>0||incoming.Method=="POST"||incoming.Method=="PUT"||incoming.Method=="PATCH"){
                        request.Content=new ByteArrayContent(body);
                        string contentType;
                        MediaTypeHeaderValue parsed;
                        if(incoming.Headers.TryGetValue("Content-Type",out contentType)&&MediaTypeHeaderValue.TryParse(contentType,out parsed))request.Content.Headers.ContentType=parsed;
                        else request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
                    }
                    using(var response=await upstream.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token))
                    using(var cancel=token.Register(()=>response.Dispose())){
                        int status=(int)response.StatusCode;
                        record.Status=status;
                        if(!response.IsSuccessStatusCode){
                            byte[] error=await Limited(await response.Content.ReadAsStreamAsync(),65536,token);
                            string code=ErrorCode(error);
                            bool rejected=status==401||status==429||((status==402||status==403)&&new[]{"usage_exceeded","insufficient_quota","insufficient_credits","credit_balance_too_low","upgrade_required"}.Contains(code));
                            if(rejected){
                                DateTimeOffset until=DateTimeOffset.UtcNow.AddSeconds(30);
                                if(status==401||code=="upgrade_required")until=DateTimeOffset.UtcNow.AddMinutes(5);
                                else if(status!=429)until=account.QuotaUntil.HasValue&&account.QuotaUntil.Value>DateTimeOffset.UtcNow?account.QuotaUntil.Value:DateTimeOffset.UtcNow.AddMinutes(5);
                                if(response.Headers.RetryAfter!=null){
                                    if(response.Headers.RetryAfter.Delta.HasValue)until=DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter.Delta.Value);
                                    else if(response.Headers.RetryAfter.Date.HasValue)until=response.Headers.RetryAfter.Date.Value;
                                }
                                Cool(account.Id,until,status==401?"凭据被拒绝":status==429?"达到请求速率限制":"额度不足或套餐不支持");
                                record.Note=code=="upgrade_required"?"套餐不支持 API，冷却并尝试下一账号":status==401?"凭据被拒绝，冷却并尝试下一账号":status==429?"明确限流，冷却并尝试下一账号":"额度不足，冷却并尝试下一账号";
                                last=status;
                                continue;
                            }
                            record.Note="上游拒绝，本次不自动重试";
                            await Failure(output,status,"upstream_rejected","上游返回 HTTP "+status+"；请检查模型、权限和请求参数");
                            committed();
                            return;
                        }
                        string type=response.Content.Headers.ContentType==null?"application/json":response.Content.Headers.ContentType.ToString();
                        if(type.IndexOf((char)13)>=0||type.IndexOf((char)10)>=0)type="application/json";
                        await Headers(output,status,type,response);
                        committed();
                        began=true;
                        string endpoint=path.Split('?')[0];
                        var meter=new UsageMeter(endpoint=="/v1/messages"||endpoint=="/v1/messages/count_tokens",type.StartsWith("text/event-stream",StringComparison.OrdinalIgnoreCase));
                        using(Stream stream=await response.Content.ReadAsStreamAsync()){
                            byte[] buffer=new byte[16384];
                            while(true){
                                int count=await stream.ReadAsync(buffer,0,buffer.Length,token);
                                if(count==0)break;
                                meter.Feed(buffer,count);
                                await output.WriteAsync(buffer,0,count,token);
                                await output.FlushAsync(token);
                            }
                        }
                        meter.End();
                        record.Input=meter.Input;
                        record.Output=meter.Output;
                        record.Note="完成";
                        return;
                    }
                }
            }
            catch(Exception){
                record.Status=token.IsCancellationRequested?499:502;
                record.Note=token.IsCancellationRequested?"请求取消或超时，已停止上游且未重放":began?"输出中断，未自动换号或重放":"连接异常，未自动重试，避免重复计费";
                throw;
            }
            finally{
                record.DurationMs=(int)watch.ElapsedMilliseconds;
                Reserve(account.Id,-1);
                Log(record);
            }
        }
        if(!compatible)throw new ProtocolFailure(400,"账号池中没有这个模型的可用账号，请先检查模型 ID 和平台账号");
        throw new ProtocolFailure(last==401?503:last,"所有兼容账号均被拒绝或额度暂不可用，请检查请求记录");
    }
    private static string ErrorCode(byte[] bytes){try{var data=Json().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));var error=Core.Object(Core.Value(data,"error"));return Core.Text(error,"code")==""?Core.Text(error,"type"):Core.Text(error,"code");}catch{return "";}}
    private static async Task<byte[]> Limited(Stream stream,int maximum,CancellationToken token){using(stream)using(var memory=new MemoryStream()){byte[] buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,0,buffer.Length,token))>0){if(memory.Length+count>maximum)throw new ProtocolFailure(502,"上游信息过大，请稍后重试");memory.Write(buffer,0,count);}return memory.ToArray();}}
    private sealed class UsageMeter {
        private readonly bool anthropic,sse;private readonly Decoder decoder=Encoding.UTF8.GetDecoder();private readonly StringBuilder text=new StringBuilder();private double? rawInput,cacheRead,cacheWrite;
        public double? Input {get{return rawInput.HasValue?rawInput+(anthropic?(cacheRead??0)+(cacheWrite??0):0):null;}}public double? Output;
        public UsageMeter(bool messages,bool stream){anthropic=messages;sse=stream;}
        public void Feed(byte[] bytes,int count){char[] chars=new char[Encoding.UTF8.GetMaxCharCount(count)];int got=decoder.GetChars(bytes,0,count,chars,0,false);if(text.Length+got>131072){text.Clear();return;}text.Append(chars,0,got);if(sse){string content=text.ToString();int from=0,at;while((at=content.IndexOf('\n',from))>=0){ParseLine(content.Substring(from,at-from));from=at+1;}if(from>0){text.Clear();text.Append(content.Substring(from));}}}
        public void End(){if(sse)ParseLine(text.ToString());else Parse(text.ToString());}
        private void ParseLine(string line){if(line.StartsWith("data: "))Parse(line.Substring(6).Trim());else if(line.StartsWith("data:"))Parse(line.Substring(5).Trim());}
        private void Parse(string content){try{var root=Json().Deserialize<Dictionary<string,object>>(content);if(root==null)return;var usage=Core.Object(Core.Value(root,"usage"));if(usage.Count==0)usage=Core.Object(Core.Value(Core.Object(Core.Value(root,"message")),"usage"));if(usage.Count==0)usage=Core.Object(Core.Value(Core.Object(Core.Value(root,"response")),"usage"));double? input=Core.Number(usage,"input_tokens")??Core.Number(usage,"prompt_tokens"),output=Core.Number(usage,"output_tokens")??Core.Number(usage,"completion_tokens");if(input.HasValue)rawInput=input;if(output.HasValue)Output=output;double? read=Core.Number(usage,"cache_read_input_tokens"),write=Core.Number(usage,"cache_creation_input_tokens");if(read.HasValue)cacheRead=read;if(write.HasValue)cacheWrite=write;}catch{}}
    }
    public void Dispose(){if(disposed)return;disposed=true;Stop();upstream.Dispose();}
}
}
