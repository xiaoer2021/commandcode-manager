using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;
namespace CommandCodeManager {
public class Settings {
    public int Interval { get; set; }
    public int PageSize { get; set; }
    public int Warning { get; set; }
    public int Critical { get; set; }
    public double LowBalance { get; set; }
    public bool Notifications { get; set; }
    public bool OfficialUsagePanel { get; set; }
    public Settings() { Interval = 300; Warning = 80; Critical = 95; LowBalance = 5; Notifications = true; }
}
public class WindowUsage {
    public double? Used { get; set; }
    public double? Cap { get; set; }
    public string ResetAt { get; set; }
    public double? Percent { get { return Used.HasValue && Cap.HasValue && Cap > 0 ? Used / Cap * 100 : null; } }
}
public class AccountData {
    public string UsageUnit { get; set; }
    public string MonthlyResetAt { get; set; }
    public string UsageSource { get; set; }
    public string UsageNote { get; set; }
    public double? CacheReadTokens { get; set; }
    public double? CacheWriteTokens { get; set; }
    public double? ReasoningTokens { get; set; }
    public ModelUsage[] Models { get; set; }
    public double? TotalTokens { get; set; }
    public double? InputTokens { get; set; }
    public double? OutputTokens { get; set; }
    public double? PeriodTokens { get; set; }
    public string TokenError { get; set; }
    public string UserId { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string OrgId { get; set; }
    public string Plan { get; set; }
    public string Status { get; set; }
    public string PeriodEnd { get; set; }
    public string PeriodStart { get; set; }
    public double? MonthlyUsed { get; set; }
    public double? MonthlyTotal { get; set; }
    public double? PeriodCost { get; set; }
    public double? RequestCount { get; set; }
    public double? Remaining { get; set; }
    public double? Monthly { get; set; }
    public double? Purchased { get; set; }
    public double? Free { get; set; }
    public bool? Limited { get; set; }
    public bool? CancelAtPeriodEnd { get; set; }
    public WindowUsage FiveHour { get; set; }
    public WindowUsage Weekly { get; set; }
    public string FetchedAt { get; set; }
    public string Error { get; set; }
}
public class Account {
    public string RenewalAt { get; set; }
    public string Platform { get; set; }
    public string ProviderId { get; set; }
    public string OpenCodeCredentialId { get; set; }
    public string CredentialState { get; set; }
    public string CredentialCheckedAt { get; set; }
    public string CredentialMessage { get; set; }
    public bool OfficialUsagePanel { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Secret { get; set; }
    public string Method { get; set; }
    public AccountData Data { get; set; }
    public string Error { get; set; }
    public List<string> AlertIds { get; set; }
    public Account() { AlertIds = new List<string>(); }
}
public class Store {
    public List<Account> Accounts { get; set; }
    public Settings Settings { get; set; }
    public Store() { Accounts = new List<Account>(); Settings = new Settings(); }
}
public class ModelUsage {public string Model{get;set;}public double Tokens{get;set;}public double Input{get;set;}public double Output{get;set;}public double CacheRead{get;set;}public double CacheWrite{get;set;}public double Reasoning{get;set;}public double Requests{get;set;}public double Cost{get;set;}}
public class Alert {
    public string Id;
    public string Text;
}
public class ApiFailure : InvalidOperationException {
    public int StatusCode { get; private set; }
    public ApiFailure(int status) : base(status==401?"登录凭据已失效":status==403?"此账号没有访问权限":"官方接口 HTTP "+status) { StatusCode=status; }
}
public static class Core {
    public static string CredentialLabel(string state) { switch(state) { case "configured":return "已配置";case "valid":return "可用";case "limited":return "用量受限";case "invalid":return "已失效";case "forbidden":return "权限不足";case "network":return "网络异常";case "error":return "检测异常";default:return "待检测"; } }
    public static string CredentialFailure(Exception error) { ApiFailure api=error as ApiFailure; if(api!=null&&api.StatusCode==401)return "invalid";if(api!=null&&api.StatusCode==403)return "forbidden";return error.Message.Contains("超时")||error.Message.Contains("无法连接")?"network":"error"; }
    public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    public static object Value(Dictionary<string, object> source, string key) { object result; return source.TryGetValue(key, out result) ? result : null; }
    public static string Text(Dictionary<string, object> source, string key) { return Convert.ToString(Value(source, key)); }
    public static double? Number(Dictionary<string, object> source, string key) {
        object value = Value(source, key);
        if (!(value is int || value is long || value is double || value is decimal || value is float)) return null;
        double number = Convert.ToDouble(value);
        return double.IsNaN(number) || double.IsInfinity(number) ? null : (double?)number;
    }
    public static bool? Boolean(Dictionary<string, object> source, string key) { object value = Value(source, key); return value is bool ? (bool?)value : null; }
    public static string Encrypt(string key) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)); }
    public static string Decrypt(string secret) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(secret), null, DataProtectionScope.CurrentUser)); }
    public static void AtomicWrite(string file, string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<Dictionary<string, object>> Get(string key, string endpoint, string query) {
        try { return await GetOnce(key, endpoint, query); }
        catch (InvalidOperationException error) {
            if (!new string[] { "官方接口 HTTP 500", "官方接口 HTTP 502", "官方接口 HTTP 503", "官方接口 HTTP 504" }.Contains(error.Message)) throw;
        }
        await Task.Delay(300); return await GetOnce(key, endpoint, query);
    }
    private static async Task<Dictionary<string, object>> GetOnce(string key, string endpoint, string query) {
        try {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://api.commandcode.ai" + endpoint + (string.IsNullOrEmpty(query) ? "" : "?" + query))) {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                using (HttpResponseMessage response = await Http.SendAsync(request)) {
                    if (!response.IsSuccessStatusCode) throw new ApiFailure((int)response.StatusCode);
                    Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                    if (data == null || Boolean(data, "success") == false) throw new InvalidOperationException("官方接口没有返回有效数据");
                    return data;
                }
            }
        } catch (HttpRequestException) { throw new InvalidOperationException("无法连接 CommandCode，请检查网络"); }
        catch (TaskCanceledException) { throw new InvalidOperationException("连接超时，请重试"); }
        catch (ArgumentException) { throw new InvalidOperationException("官方数据格式异常"); }
    }
    public static WindowUsage Window(Dictionary<string, object> parent, string name) {
        Dictionary<string, object> value = Object(Value(parent, name));
        return value.Count == 0 ? null : new WindowUsage { Used = Number(value, "used"), Cap = Number(value, "cap"), ResetAt = NormalizeTime(Text(value, "resetAt")) };
    }
    public static bool TryTime(string value, out DateTimeOffset time) {
        time = DateTimeOffset.MinValue; double numeric;
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out numeric)) {
            if (numeric <= 0 || numeric > 253402300799999) return false;
            try { time = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(numeric >= 100000000000 ? numeric : numeric * 1000); return true; } catch { return false; }
        }
        return DateTimeOffset.TryParse(value, out time);
    }
    public static string NormalizeTime(string value) { DateTimeOffset time; return value == "0" ? "0" : TryTime(value, out time) ? time.ToUniversalTime().ToString("o") : value; }
    public static string PlanLabel(string raw) {
        string value = (raw ?? "").ToLowerInvariant().Replace('_', '-'); string scope = value.StartsWith("individual-") ? " · 个人版" : value.StartsWith("team-") || value.StartsWith("teams-") ? " · 团队版" : "";
        string name = value.Replace("individual-", "").Replace("teams-", "").Replace("team-", "");
        if (name.StartsWith("goat")) return "GOAT" + scope; if (name == "go" || name.StartsWith("go-v")) return "Go" + scope; if (name.StartsWith("pro")) return "Pro" + scope;
        if (name.StartsWith("max")) return name.Replace("max", "Max").Replace("-", " ") + scope; if (name == "free") return "免费版"; if (name == "provider") return "Provider"; if (name.Contains("enterprise")) return "企业版";
        return string.IsNullOrEmpty(raw) ? "无订阅信息" : raw;
    }
    public static string StatusLabel(string raw) { switch (raw) { case "active": return "有效"; case "trialing": return "试用中"; case "canceled": return "已取消"; case "past_due": return "待付款"; case "paused": return "已暂停"; case "incomplete": return "待激活"; case "unpaid": return "未付款"; default: return string.IsNullOrEmpty(raw) ? "无订阅" : "其他状态"; } }
    public static double? PlanAllowance(string raw) {
        string plan = (raw ?? "").ToLowerInvariant().Replace('_', '-');
        string[] ids = { "individual-pro-v1", "individual-go-v1", "individual-provider", "individual-goat", "individual-ultra", "individual-pro", "individual-max", "individual-go", "teams-pro" }; double[] credits = { 80, 10, 15, 70, 300, 30, 150, 10, 40 };
        for (int i = 0; i < ids.Length; i++) if (plan.StartsWith(ids[i], StringComparison.Ordinal)) return credits[i]; return null;
    }
    public static void ProjectMonthly(AccountData data) { double? allowance = PlanAllowance(data.Plan); if (allowance.HasValue && data.Monthly.HasValue && (data.Status == "active" || data.Status == "trialing")) { data.MonthlyTotal = Math.Max(allowance.Value, data.Monthly.Value); data.MonthlyUsed = Math.Max(0, data.MonthlyTotal.Value - data.Monthly.Value); } else { data.MonthlyTotal = null; data.MonthlyUsed = null; } }
    private static async Task<Dictionary<string, object>> SafeGet(string key, string endpoint, string query, string label, List<string> errors) { try { return await Get(key, endpoint, query); } catch (Exception e) { lock (errors) errors.Add(label + "：" + e.Message); return new Dictionary<string, object>(); } }
    public static AccountData Normalize(Dictionary<string, object> identity, Dictionary<string, object> credits, Dictionary<string, object> subscription) {
        Dictionary<string, object> user = Object(Value(identity, "user"));
        Dictionary<string, object> org = Object(Value(identity, "org"));
        Dictionary<string, object> credit = Object(Value(credits, "credits"));
        Dictionary<string, object> sub = Object(Value(subscription, "data"));
        Dictionary<string, object> windows = Object(Value(credits, "windowLimits"));
        double? monthly = Number(credit, "monthlyCredits"), purchased = Number(credit, "purchasedCredits"), free = Number(credit, "freeCredits");
        return new AccountData { UserId = Text(user, "id"), UserName = Text(user, "userName"), Email = Text(user, "email"), OrgId = Text(org, "id"), Plan = Text(sub, "planId") == "" ? Text(credit, "planId") : Text(sub, "planId"), Status = Text(sub, "status"), PeriodEnd = Text(sub, "currentPeriodEnd"), Monthly = monthly, Purchased = purchased, Free = free, Remaining = monthly.HasValue || purchased.HasValue || free.HasValue ? (double?)(monthly.GetValueOrDefault() + purchased.GetValueOrDefault() + free.GetValueOrDefault()) : null, Limited = Boolean(windows, "limited"), CancelAtPeriodEnd = Boolean(sub, "cancelAtPeriodEnd"), FiveHour = Window(windows, "fiveHour"), Weekly = Window(windows, "weekly"), FetchedAt = DateTimeOffset.UtcNow.ToString("o") };
    }
    public static async Task<AccountData> Fetch(string key, Action<string> progress = null, Action<Dictionary<string,object>> checkIdentity = null) {
        if(progress!=null)progress("正在验证账号身份…");
        Dictionary<string, object> identity = await Get(key, "/alpha/whoami", "limits=1");
        if (Text(Object(Value(identity, "user")), "id") == "") throw new InvalidOperationException("无法确认账号身份");
        if(checkIdentity!=null)checkIdentity(identity);
        if(progress!=null)progress("正在同步额度和订阅…");
        string org = Text(Object(Value(identity, "org")), "id");
        string query = org == "" ? "" : "orgId=" + Uri.EscapeDataString(org);
        Dictionary<string, object> credits = new Dictionary<string, object>(), subscription = new Dictionary<string, object>();
        List<string> errors = new List<string>();
        Task<Dictionary<string, object>> creditTask = SafeGet(key, "/alpha/billing/credits", query, "额度", errors), subscriptionTask = SafeGet(key, "/alpha/billing/subscriptions", query, "套餐", errors);
        await Task.WhenAll(creditTask, subscriptionTask); credits = creditTask.Result; subscription = subscriptionTask.Result;
        AccountData data = Normalize(identity, credits, subscription);
        data.PeriodStart = Text(Object(Value(subscription, "data")), "currentPeriodStart");
        if(progress!=null)progress("正在同步 Token 用量…");
        if (!string.IsNullOrEmpty(data.PeriodStart)) {
            Dictionary<string, object> summary = await SafeGet(key, "/alpha/usage/summary", query + (query == "" ? "" : "&") + "since=" + Uri.EscapeDataString(data.PeriodStart), "月度用量", errors);
            data.PeriodCost = Number(summary, "totalCost"); data.RequestCount = Number(summary, "totalCount");
            data.PeriodTokens = Number(summary, "totalTokens");
            ProjectMonthly(data);
        }
        try {
            Dictionary<string, object> totals = await Get(key, "/alpha/usage/summary", query + (query == "" ? "" : "&") + "since=1970-01-01T00%3A00%3A00Z");
            data.TotalTokens = Number(totals, "totalTokens"); data.InputTokens = Number(totals, "totalTokensIn"); data.OutputTokens = Number(totals, "totalTokensOut");
            if (!data.TotalTokens.HasValue) data.TokenError = "官方未返回 Token 统计";
        } catch (Exception error) { data.TokenError = error.Message; }
        data.Error = string.Join("；", errors);
        return data;
    }
    public static List<Alert> Alerts(Account account, Settings settings, DateTimeOffset now) {
        List<Alert> result = new List<Alert>();
        DateTimeOffset manualEnd;
        if(TryTime(account.RenewalAt,out manualEnd)) { int manualDays=(int)Math.Ceiling((manualEnd-now).TotalDays);int manualBucket=manualDays<=0?0:manualDays<=1?1:manualDays<=3?3:manualDays<=7?7:-1;if(manualBucket>=0)result.Add(new Alert{Id="renewal:"+manualBucket+":"+account.RenewalAt,Text=manualDays<=0?"已到手动记录的续费时间":"手动记录的续费日期将在 "+manualDays+" 天内到达"}); }
        AccountData data = account.Data;
        if (data == null || !string.IsNullOrEmpty(account.Error) || !string.IsNullOrEmpty(data.Error)) return result;
        WindowUsage[] windows = { data.FiveHour, data.Weekly };
        string[] labels = { "5 小时", "每周" };
        for (int i = 0; i < windows.Length; i++) {
            WindowUsage window = windows[i];
            DateTimeOffset reset;
            if (data.Limited != true || window == null || !window.Percent.HasValue || TryTime(window.ResetAt, out reset) && reset <= now) continue;
            int threshold = window.Percent >= settings.Critical ? settings.Critical : window.Percent >= settings.Warning ? settings.Warning : 0;
            if (threshold > 0) result.Add(new Alert { Id = i + ":" + threshold + ":" + window.ResetAt, Text = labels[i] + "额度已用 " + window.Percent.Value.ToString("0") + "%" });
        }
        if (data.Remaining.HasValue && data.Remaining <= settings.LowBalance) result.Add(new Alert { Id = "balance", Text = "剩余额度 " + Money(data.Remaining) });
        if (data.MonthlyUsed.HasValue && data.MonthlyTotal.HasValue && data.MonthlyTotal > 0) { double percent = data.MonthlyUsed.Value / data.MonthlyTotal.Value * 100; int threshold = percent >= settings.Critical ? settings.Critical : percent >= settings.Warning ? settings.Warning : 0; if (threshold > 0) result.Add(new Alert { Id = "monthly:" + threshold + ":" + data.PeriodEnd, Text = "月度额度已用 " + percent.ToString("0") + "%" }); }
        DateTimeOffset end;
        if (TryTime(data.PeriodEnd, out end)) {
            int days = (int)Math.Ceiling((end - now).TotalDays);
            int bucket = days <= 1 ? 1 : days <= 3 ? 3 : days <= 7 ? 7 : 0;
            if (bucket > 0) result.Add(new Alert { Id = "period:" + bucket + ":" + data.PeriodEnd, Text = days <= 0 ? "套餐周期已结束，请刷新确认" : "套餐周期将在 " + days + " 天内结束" });
        }
        return result;
    }
    public static string Money(double? amount) { return amount.HasValue ? "$" + amount.Value.ToString("0.00") : "未获取"; }
    public static string Date(string value) { DateTimeOffset result; return value == "0" ? "首次使用后开始计时" : TryTime(value, out result) ? result.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm") : "接口未提供"; }
    public static void Switch(string file, string key, AccountData data) {
        byte[] before = File.Exists(file) ? File.ReadAllBytes(file) : null;
        Dictionary<string, object> next;
        try { next = before == null ? new Dictionary<string, object>() : Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(before)); }
        catch { throw new InvalidOperationException("本机登录文件无法解析，已保留原文件"); }
        if (next == null) throw new InvalidOperationException("本机登录文件格式异常");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        if (before != null) File.WriteAllBytes(file + ".native-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".encrypted", ProtectedData.Protect(before, null, DataProtectionScope.CurrentUser));
        next["apiKey"] = key; next["userId"] = data.UserId; next["userName"] = data.UserName; next["keyName"] = "account-manager"; next["authenticatedAt"] = DateTimeOffset.UtcNow.ToString("o");
        try {
            AtomicWrite(file, Json.Serialize(next));
            Dictionary<string, object> readback = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
            if (Text(readback, "apiKey") != key || Text(readback, "userId") != data.UserId) throw new InvalidOperationException("切换回读失败");
        } catch {
            if (before != null) AtomicWrite(file, Encoding.UTF8.GetString(before)); else if (File.Exists(file)) File.Delete(file);
            throw;
        }
    }
}
public sealed class LoginFlow : IDisposable {
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private bool disposed;
    public readonly string State;
    public readonly string Url;
    public LoginFlow() {
        byte[] bytes = new byte[32]; using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        State = Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        string callback = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/callback";
        Url = "https://commandcode.ai/studio/auth/cli?callback=" + Uri.EscapeDataString(callback) + "&state=" + State + "&mode=redirect";
        stop.CancelAfter(TimeSpan.FromMinutes(5)); stop.Token.Register(() => listener.Stop());
    }
    private static async Task Send(NetworkStream stream, int status, string body, string origin, bool options) {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string headers = "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Response") + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n";
        if (origin == "https://commandcode.ai") headers += "Access-Control-Allow-Origin: https://commandcode.ai\r\n";
        if (options) headers += "Access-Control-Allow-Methods: POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
        byte[] head = Encoding.ASCII.GetBytes(headers + "\r\n"); await stream.WriteAsync(head, 0, head.Length); await stream.WriteAsync(bytes, 0, bytes.Length);
    }
    public async Task<string> Wait() {
        try {
            while (!stop.IsCancellationRequested) {
                using (TcpClient client = await listener.AcceptTcpClientAsync()) {
                    NetworkStream stream = client.GetStream();
                    byte[] buffer = new byte[4096]; MemoryStream input = new MemoryStream(); int headerEnd = -1; int length = 0;
                    Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); string method = "", route = "";
                    while (input.Length < 49152) {
                        Task<int> read = stream.ReadAsync(buffer, 0, buffer.Length);
                        if (await Task.WhenAny(read, Task.Delay(10000, stop.Token)) != read) break;
                        int count = await read; if (count == 0) break; input.Write(buffer, 0, count);
                        string head = Encoding.ASCII.GetString(input.ToArray());
                        if (headerEnd < 0 && (headerEnd = head.IndexOf("\r\n\r\n", StringComparison.Ordinal)) >= 0) {
                            string[] lines = head.Substring(0, headerEnd).Split(new string[] { "\r\n" }, StringSplitOptions.None); string[] request = lines[0].Split(' ');
                            if (request.Length < 2) break; method = request[0]; route = request[1];
                            foreach (string line in lines.Skip(1)) { int colon = line.IndexOf(':'); if (colon > 0) headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim(); }
                            string contentLength; if (headers.TryGetValue("Content-Length", out contentLength)) int.TryParse(contentLength, out length);
                            if (length < 0 || length > 32768) break;
                        }
                        if (headerEnd >= 0 && input.Length >= headerEnd + 4 + length) break;
                    }
                    string origin; headers.TryGetValue("Origin", out origin);
                    string contentType; headers.TryGetValue("Content-Type", out contentType);
                    bool formNavigation = method == "POST" && origin == "null" && contentType != null && contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
                    if (origin != null && origin != "https://commandcode.ai" && !formNavigation) { await Send(stream, 403, "授权来源未通过校验。请返回客户端重新发起官方登录。", origin, false); continue; }
                    if (route != "/callback" || headerEnd < 0 || length > 32768 || length < 0 || input.Length < headerEnd + 4 + length) { await Send(stream, 400, "Invalid request", origin, false); continue; }
                    if (method == "OPTIONS") { await Send(stream, 200, "", origin, true); continue; }
                    if (method != "POST") { await Send(stream, 405, "POST required", origin, false); continue; }
                    string body = Encoding.UTF8.GetString(input.ToArray(), headerEnd + 4, length); Dictionary<string, object> data = null;
                    try {
                        if (contentType != null && contentType.StartsWith("application/json")) data = Core.Json.Deserialize<Dictionary<string, object>>(body);
                        else { data = new Dictionary<string, object>(); foreach (string pair in body.Split('&')) { string[] kv = pair.Split(new char[] { '=' }, 2); if (kv.Length == 2) data[Uri.UnescapeDataString(kv[0].Replace("+", " "))] = Uri.UnescapeDataString(kv[1].Replace("+", " ")); } }
                    } catch { data = null; }
                    if (data == null || Core.Text(data, "state") != State) { await Send(stream, 403, "Invalid state", origin, false); continue; }
                    if (Core.Text(data, "error") != "") { await Send(stream, 200, "授权已取消", origin, false); throw new InvalidOperationException("官方登录授权已取消"); }
                    if (new string[] { "apiKey", "userId", "userName", "keyName" }.Any(name => Core.Text(data, name) == "")) { await Send(stream, 400, "Missing fields", origin, false); continue; }
                    await Send(stream, 200, "<!doctype html><meta charset='utf-8'><title>登录完成</title><h2>登录完成，请返回 CommandCode 账号管理器。</h2>", origin, false);
                    return Core.Text(data, "apiKey");
                }
            }
        } catch (SocketException) { if (!stop.IsCancellationRequested) throw; }
        catch (ObjectDisposedException) { if (!stop.IsCancellationRequested) throw; }
        throw new InvalidOperationException("登录已取消或超时，请重试");
    }
    public void Dispose() { if (disposed) return; disposed = true; stop.Cancel(); listener.Stop(); stop.Dispose(); }
}
public class ManagerForm : Form {
    private readonly string file;
    private readonly string authFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".commandcode", "auth.json");
    private Store store;
    private readonly DataGridView grid = new SmoothGrid();
    private readonly SummaryStrip summary = new SummaryStrip();
    private readonly Label status = new Label(), detail = new Label(), pageLabel = new Label();
    private readonly TextBox search = new TextBox();
    private readonly Button refresh = new ModernButton(), previous = new ModernButton(), next = new ModernButton();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private bool busy, quitting;
    private int page;
    private LoginFlow login;
    private bool dragging;
    private Point dragOrigin, windowOrigin;
    public bool TestMode;
    public ManagerForm(string dataFile, bool testMode) {
        file = dataFile; TestMode = testMode;
        store = File.Exists(file) ? Core.Json.Deserialize<Store>(File.ReadAllText(file)) : new Store();
        if (store == null || store.Accounts == null || store.Settings == null) throw new InvalidOperationException("账号数据格式异常，原文件已保留");
        Text = "CommandCode 账号管理 · 轻量版"; Font = new Font("Microsoft YaHei UI", 9.5f); BackColor = Color.FromArgb(225, 232, 242); ForeColor = PaintStyle.Ink;
        FormBorderStyle = FormBorderStyle.None; Padding = new Padding(1); ClientSize = new Size(1180, 680); MinimumSize = new Size(1000, 610); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.None; MaximizedBounds = Screen.FromControl(this).WorkingArea;
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 14), RowCount = 6, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        Controls.Add(layout);
        Panel header = new Panel { Dock = DockStyle.Fill };
        Label logo = new Label { Text = "C", BackColor = PaintStyle.Blue, ForeColor = Color.White, Font = new Font("Segoe UI", 19, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Size = new Size(42, 42), Location = new Point(0, 3) };
        Label title = new Label { Text = "CommandCode  /  账号总览", Font = new Font(Font.FontFamily, 16, FontStyle.Bold), AutoSize = true, Location = new Point(56, 1) };
        Label subtitle = new Label { Text = "用量一目了然，账号随时切换", AutoSize = true, ForeColor = PaintStyle.Muted, Location = new Point(57, 34) };
        ModernButton minimize = new ModernButton { Text = "—", Size = new Size(35, 30) }, maximize = new ModernButton { Text = "□", Size = new Size(35, 30) }, close = new ModernButton { Text = "×", Size = new Size(35, 30) };
        minimize.Click += (s, e) => WindowState = FormWindowState.Minimized; maximize.Click += (s, e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; close.Click += (s, e) => Close();
        header.SizeChanged += (s, e) => { minimize.Location = new Point(header.Width - 120, 1); maximize.Location = new Point(header.Width - 80, 1); close.Location = new Point(header.Width - 40, 1); };
        foreach (Control dragSurface in new Control[] { header, title, subtitle, logo }) {
            dragSurface.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { BeginDrag(Cursor.Position); ((Control)s).Capture = true; } };
            dragSurface.MouseMove += (s, e) => { if (dragging && (Control.MouseButtons & MouseButtons.Left) != 0) MoveDrag(Cursor.Position); };
            dragSurface.MouseUp += (s, e) => { dragging = false; ((Control)s).Capture = false; };
            dragSurface.MouseCaptureChanged += (s, e) => { if (!((Control)s).Capture) dragging = false; };
        }
        header.Controls.AddRange(new Control[] { logo, title, subtitle, minimize, maximize, close }); layout.Controls.Add(header, 0, 0);
        summary.Dock = DockStyle.Fill; layout.Controls.Add(summary, 0, 1);
        FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        tools.Controls.Add(Button("添加账号", async () => await AddDialog(), 100)); tools.Controls.Add(Button("导入本机", async () => await Add(LocalKey(), "本机账号", "import"), 94));
        refresh.Text = "刷新用量"; refresh.Size = new Size(94, 32); refresh.Click += async (s, e) => await Safe(async () => await RefreshAll()); tools.Controls.Add(refresh);
        tools.Controls.Add(Button("监控设置", () => { SettingsDialog(); return Task.FromResult(0); }, 94));
        tools.Controls.Add(Button("移除选中", () => { Remove(); return Task.FromResult(0); }, 94));
        search.Width = 230; search.Margin = new Padding(18, 3, 0, 0); search.TextChanged += (s, e) => { page = 0; Render(); }; tools.Controls.Add(search);
        Label searchHint = new Label { Text = "搜索账号 / 邮箱", AutoSize = true, Margin = new Padding(6, 7, 0, 0), ForeColor = Color.SlateGray }; tools.Controls.Add(searchHint); layout.Controls.Add(tools, 0, 2);
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false; grid.RowHeadersVisible = false; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.ScrollBars = ScrollBars.None; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.ColumnHeadersHeight = 36; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.RowTemplate.Height = 48; grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234, 239, 246); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(66, 80, 100); grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold); grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(227, 237, 255); grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 48, 85); grid.DefaultCellStyle.Padding = new Padding(7, 0, 3, 0); grid.GridColor = Color.FromArgb(232, 237, 244); grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 253);
        string[] names = { "账号", "套餐 / 状态", "剩余额度", "5 小时用量", "每周用量", "套餐周期结束", "提醒", "切换" }; float[] weights = { 170, 96, 85, 118, 118, 108, 105, 72 };
        for (int i = 0; i < names.Length; i++) { DataGridViewColumn column = i == 7 ? (DataGridViewColumn)new DataGridViewButtonColumn { FlatStyle = FlatStyle.Flat } : new DataGridViewTextBoxColumn(); column.HeaderText = names[i]; column.FillWeight = weights[i]; column.SortMode = DataGridViewColumnSortMode.NotSortable; column.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.Columns.Add(column); }
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(206, 218, 235); grid.ColumnHeadersDefaultCellStyle.ForeColor = PaintStyle.Ink; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(208, 225, 250); grid.DefaultCellStyle.SelectionForeColor = PaintStyle.Ink; grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(235, 241, 249); grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.RowTemplate.Height = 52; grid.Columns[7].FillWeight = 95; grid.CellPainting += PaintCell;
        grid.SelectionChanged += (s, e) => Details(); grid.SizeChanged += (s, e) => Render(); grid.CellContentClick += async (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 7) await Safe(async () => await Switch((Account)grid.Rows[e.RowIndex].Tag)); };
        layout.Controls.Add(grid, 0, 3);
        Panel pages = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        FlowLayoutPanel pageControls = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        previous.Text = "上一页"; previous.Size = new Size(76, 28); previous.Click += (s, e) => { page--; Render(); }; next.Text = "下一页"; next.Size = new Size(76, 28); next.Click += (s, e) => { page++; Render(); }; pageLabel.AutoSize = true; pageLabel.Margin = new Padding(12, 5, 12, 0);
        pageControls.Controls.Add(previous); pageControls.Controls.Add(pageLabel); pageControls.Controls.Add(next); pages.Controls.Add(pageControls); layout.Controls.Add(pages, 0, 4);
        TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 }; footer.RowStyles.Add(new RowStyle(SizeType.Percent, 70)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 30)); detail.Dock = DockStyle.Fill; detail.BackColor = Color.White; detail.Padding = new Padding(8); detail.AutoEllipsis = true; detail.DoubleClick += (s, e) => MessageBox.Show(this, detail.Text, "账号详情", MessageBoxButtons.OK, MessageBoxIcon.Information); status.Dock = DockStyle.Fill; status.ForeColor = Color.SlateGray; status.Padding = new Padding(0, 5, 0, 0); footer.Controls.Add(detail, 0, 0); footer.Controls.Add(status, 0, 1); layout.Controls.Add(footer, 0, 5);
        tray.Icon = SystemIcons.Application; tray.Text = "CommandCode 账号管理"; tray.Visible = !testMode; ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("打开账号总览", null, (s, e) => Restore()); menu.Items.Add("刷新用量", null, async (s, e) => await Safe(async () => await RefreshAll())); menu.Items.Add("退出", null, (s, e) => { quitting = true; Close(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += (s, e) => Restore(); tray.BalloonTipClicked += (s, e) => Restore();
        timer.Tick += async (s, e) => await Safe(async () => await RefreshAll()); Schedule();
        FormClosing += (s, e) => { if (!quitting && !TestMode) { e.Cancel = true; Hide(); } else { timer.Stop(); if (login != null) login.Dispose(); tray.Dispose(); } };
        Shown += async (s, e) => { Render(); if (!TestMode && store.Accounts.Count > 0) await Safe(async () => await RefreshAll()); };
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
    private void BeginDrag(Point cursor) { if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal; dragging = true; dragOrigin = cursor; windowOrigin = Location; }
    private void MoveDrag(Point cursor) { if (dragging) Location = new Point(windowOrigin.X + cursor.X - dragOrigin.X, windowOrigin.Y + cursor.Y - dragOrigin.Y); }
    protected override void WndProc(ref Message message) { base.WndProc(ref message); if (message.Msg == 0x84 && WindowState == FormWindowState.Normal && message.Result == new IntPtr(1)) { Point p = PointToClient(Cursor.Position); int hit = p.X < 6 ? p.Y < 6 ? 13 : p.Y > Height - 7 ? 16 : 10 : p.X > Width - 7 ? p.Y < 6 ? 14 : p.Y > Height - 7 ? 17 : 11 : p.Y < 6 ? 12 : p.Y > Height - 7 ? 15 : 1; message.Result = new IntPtr(hit); } }
    private void PaintCell(object sender, DataGridViewCellPaintingEventArgs e) {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        e.PaintBackground(e.ClipBounds, true); Rectangle b = e.CellBounds; Account a = grid.Rows[e.RowIndex].Tag as Account; if (a == null) return; AccountData d = a.Data ?? new AccountData(); string text = Convert.ToString(e.FormattedValue);
        using (Font small = new Font(Font.FontFamily, 9f)) using (Font bold = new Font(Font.FontFamily, 9f, FontStyle.Bold)) using (Font amount = new Font("Segoe UI", 13, FontStyle.Bold)) {
            if (e.ColumnIndex == 7) { Rectangle button = new Rectangle(b.X + 7, b.Y + 9, b.Width - 14, 34); bool current = text == "当前"; PaintStyle.Box(e.Graphics, button, current ? Color.FromArgb(40, 112, 75) : PaintStyle.Blue, Color.Transparent, 7); PaintStyle.Text(e.Graphics, current ? "正在使用" : "切换账号", bold, Color.White, button, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine); }
            else if (e.ColumnIndex == 2) PaintStyle.Text(e.Graphics, text, amount, PaintStyle.Ink, new Rectangle(b.X + 10, b.Y, b.Width - 18, b.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            else if (e.ColumnIndex == 3 || e.ColumnIndex == 4) { WindowUsage usage = e.ColumnIndex == 3 ? d.FiveHour : d.Weekly; string[] lines = text.Split('\n'); PaintStyle.Text(e.Graphics, lines[0], small, PaintStyle.Ink, new Rectangle(b.X + 9, b.Y + 7, b.Width - 17, 16), TextFormatFlags.SingleLine); if (usage != null && usage.Percent.HasValue && d.Limited != false) { Rectangle bar = new Rectangle(b.X + 9, b.Y + 26, b.Width - 19, 4); using (SolidBrush background = new SolidBrush(Color.FromArgb(229, 235, 246))) e.Graphics.FillRectangle(background, bar); bar.Width = (int)(bar.Width * Math.Min(100, Math.Max(0, usage.Percent.Value)) / 100); using (SolidBrush fill = new SolidBrush(usage.Percent >= store.Settings.Critical ? Color.FromArgb(219, 79, 83) : usage.Percent >= store.Settings.Warning ? Color.FromArgb(222, 155, 48) : PaintStyle.Blue)) e.Graphics.FillRectangle(fill, bar); } if (lines.Length > 1) PaintStyle.Text(e.Graphics, lines[1].Replace("重置 ", ""), small, PaintStyle.Muted, new Rectangle(b.X + 9, b.Y + 34, b.Width - 17, 14), TextFormatFlags.SingleLine); }
            else if (e.ColumnIndex == 6) { bool normal = text == "正常"; Rectangle badge = new Rectangle(b.X + 7, b.Y + 13, b.Width - 14, 26); PaintStyle.Box(e.Graphics, badge, normal ? Color.FromArgb(234, 247, 239) : Color.FromArgb(255, 244, 222), Color.Transparent, 6); string label = normal ? "正常" : text == "同步异常" ? "同步异常" : text.Contains("周期") ? "周期提醒" : text.Contains("剩余") ? "余额不足" : "额度预警"; PaintStyle.Text(e.Graphics, label, small, normal ? Color.FromArgb(45, 132, 88) : Color.FromArgb(165, 113, 32), badge, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine); }
            else { string[] lines = text.Split('\n'); PaintStyle.Text(e.Graphics, lines[0], e.ColumnIndex == 0 ? bold : small, PaintStyle.Ink, new Rectangle(b.X + 10, b.Y + (lines.Length > 1 ? 8 : 17), b.Width - 18, 17), TextFormatFlags.SingleLine); if (lines.Length > 1) PaintStyle.Text(e.Graphics, lines[1], small, PaintStyle.Muted, new Rectangle(b.X + 10, b.Y + 29, b.Width - 18, 16), TextFormatFlags.SingleLine); }
        }
        using (Pen line = new Pen(Color.FromArgb(191, 203, 220))) e.Graphics.DrawLine(line, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1); e.Handled = true;
    }
    private Button Button(string text, Func<Task> action, int width) { Button result = new ModernButton { Text = text, Size = new Size(width, 32), Primary = text == "添加账号", BackColor = Color.White, Margin = new Padding(0, 0, 8, 0) }; result.Click += async (s, e) => await Safe(action); return result; }
    private async Task Safe(Func<Task> action) { try { await action(); } catch (Exception e) { MessageBox.Show(this, e is CryptographicException ? "凭据无法解密，请使用原 Windows 用户或重新添加账号" : e.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
    private void Save() { Core.AtomicWrite(file, Core.Json.Serialize(store)); }
    private void Schedule() { timer.Stop(); timer.Interval = Math.Max(60, Math.Min(3600, store.Settings.Interval)) * 1000; if (store.Accounts.Count > 0 && !TestMode) timer.Start(); }
    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); Render(); }
    private string LocalKey() { if (!File.Exists(authFile)) return ""; try { return Core.Text(Core.Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(authFile)), "apiKey"); } catch { return ""; } }
    private Account Selected() { return grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as Account : null; }
    private static string WindowText(WindowUsage value) { if (value == null || !value.Percent.HasValue) return "未获取"; DateTimeOffset reset; if (DateTimeOffset.TryParse(value.ResetAt, out reset) && reset <= DateTimeOffset.UtcNow) return "待刷新\n窗口已结束"; return value.Percent.Value.ToString("0") + "%  " + Core.Money(value.Used) + "/" + Core.Money(value.Cap) + "\n重置 " + Core.Date(value.ResetAt); }
    public void Render() {
        if (!Visible || grid.Columns.Count == 0) return;
        List<Account> filtered = store.Accounts.Where(a => (a.Name + " " + (a.Data == null ? "" : a.Data.Email + " " + a.Data.Plan)).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        int capacity = Math.Max(1, (grid.ClientSize.Height - grid.ColumnHeadersHeight - 3) / 52); int pages = Math.Max(1, (filtered.Count + capacity - 1) / capacity); page = Math.Max(0, Math.Min(page, pages - 1));
        Account selected = Selected(); string selectedId = selected == null ? "" : selected.Id; string key = LocalKey(); grid.SuspendLayout(); grid.Rows.Clear();
        foreach (Account a in filtered.Skip(page * capacity).Take(capacity)) {
            AccountData d = a.Data ?? new AccountData(); List<Alert> alerts = Core.Alerts(a, store.Settings, DateTimeOffset.UtcNow); string error = string.IsNullOrEmpty(a.Error) ? d.Error : a.Error; bool active = !string.IsNullOrEmpty(key) && key == Core.Decrypt(a.Secret);
            string state = d.Status == "active" ? "有效" : d.Status == "canceled" ? "已取消" : string.IsNullOrEmpty(d.Status) ? "未获取" : d.Status;
            int row = grid.Rows.Add(a.Name + (active ? " · 当前" : "") + "\n" + d.Email, (string.IsNullOrEmpty(d.Plan) ? "未获取" : d.Plan) + "\n" + state, Core.Money(d.Remaining), d.Limited == false ? "不限窗口" : WindowText(d.FiveHour), d.Limited == false ? "不限窗口" : WindowText(d.Weekly), Core.Date(d.PeriodEnd) + (d.CancelAtPeriodEnd == true ? "\n到期取消" : d.CancelAtPeriodEnd == false ? "\n到期续费" : ""), !string.IsNullOrEmpty(error) ? "同步异常" : alerts.Count > 0 ? alerts[0].Text : "正常", active ? "当前" : "切换");
            grid.Rows[row].Tag = a; if (active) grid.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(237, 246, 255); if (alerts.Count > 0 || !string.IsNullOrEmpty(error)) grid.Rows[row].Cells[6].Style.ForeColor = Color.FromArgb(174, 95, 15); grid.Rows[row].Cells[6].ToolTipText = !string.IsNullOrEmpty(error) ? error : string.Join("；", alerts.Select(value => value.Text));
            if (a.Id == selectedId) grid.Rows[row].Selected = true;
        }
        grid.ResumeLayout(); previous.Enabled = page > 0; next.Enabled = page < pages - 1; pageLabel.Text = "第 " + (page + 1) + " / " + pages + " 页 · " + filtered.Count + " 个账号";
        List<Account> known = store.Accounts.Where(a => a.Data != null && a.Data.Remaining.HasValue && string.IsNullOrEmpty(a.Error) && string.IsNullOrEmpty(a.Data.Error)).ToList();
        int attention = store.Accounts.Count(a => !string.IsNullOrEmpty(a.Error) || a.Data != null && !string.IsNullOrEmpty(a.Data.Error) || Core.Alerts(a, store.Settings, DateTimeOffset.UtcNow).Count > 0);
        summary.UpdateValues(store.Accounts.Count, known.Count > 0 ? Core.Money(known.Sum(a => a.Data.Remaining.Value)) : "—", attention, known.Count);
        status.Text = busy ? "正在同步账号…" : "每 " + (store.Settings.Interval / 60.0).ToString("0.#") + " 分钟刷新 · 关闭窗口后托盘监控 · 新会话使用切换后的账号"; refresh.Enabled = !busy; Details();
    }
    private void Details() {
        Account a = Selected();
        if (a == null) { detail.Text = "添加账号：支持官方邮箱密码登录和 API Key。账号较多时分页展示，无需滚动。\n套餐周期结束与窗口额度重置分开显示；购买额度不会因周期结束而失效。"; return; }
        AccountData d = a.Data ?? new AccountData(); string error = string.IsNullOrEmpty(a.Error) ? d.Error : a.Error;
        string alerts = string.Join("；", Core.Alerts(a, store.Settings, DateTimeOffset.UtcNow).Select(value => value.Text));
        detail.Text = "周期余额 " + Core.Money(d.Monthly) + "  ·  购买余额 " + Core.Money(d.Purchased) + "  ·  免费余额 " + Core.Money(d.Free) + "  ·  最后同步 " + Core.Date(d.FetchedAt) + "\n" + (!string.IsNullOrEmpty(error) ? "同步异常：" + error + "（显示上次已获取数据）" : string.IsNullOrEmpty(alerts) ? "当前没有预警。套餐周期不代表账号或额外购买额度失效。" : alerts);
    }
    private async Task Add(string key, string name, string method) {
        if (busy) throw new InvalidOperationException("正在同步，请稍后再试");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("未获取登录凭据，请添加 API Key 或先登录本机 CommandCode");
        key = key.Trim(); if (key.Length > 8192 || key.Contains("\n") || key.Contains("\r")) throw new InvalidOperationException("API Key 格式不正确");
        busy = true; Render();
        try {
            AccountData data = await Core.Fetch(key);
            if (store.Accounts.Any(a => a.Data != null && a.Data.UserId == data.UserId && a.Data.OrgId == data.OrgId)) throw new InvalidOperationException("这个账号已经添加");
            Account account = new Account { Id = Guid.NewGuid().ToString("N"), Name = string.IsNullOrWhiteSpace(name) ? data.Email : name, Secret = Core.Encrypt(key), Method = method, Data = data }; store.Accounts.Add(account); Notify(account); Save(); Schedule();
        } finally { busy = false; Render(); }
    }
    private void Notify(Account a) {
        List<Alert> alerts = Core.Alerts(a, store.Settings, DateTimeOffset.UtcNow); List<Alert> fresh = alerts.Where(value => a.AlertIds == null || !a.AlertIds.Contains(value.Id)).ToList();
        a.AlertIds = alerts.Select(value => value.Id).ToList();
        if (store.Settings.Notifications && fresh.Count > 0 && !TestMode) { tray.BalloonTipTitle = "CommandCode · " + a.Name; tray.BalloonTipText = string.Join("；", fresh.Select(value => value.Text)); tray.ShowBalloonTip(5000); }
    }
    private async Task RefreshAll() {
        if (busy || store.Accounts.Count == 0) return;
        busy = true; Render();
        try {
            foreach (Account a in store.Accounts.ToArray()) { try { a.Data = await Core.Fetch(Core.Decrypt(a.Secret)); a.Error = null; Notify(a); } catch (Exception e) { a.Error = e is CryptographicException ? "凭据无法解密，请重新添加" : e.Message; } }
            Save();
        } finally { busy = false; Render(); }
    }
    private async Task Switch(Account a) {
        if (busy) throw new InvalidOperationException("正在同步，请稍后再切换");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMMAND_CODE_API_KEY"))) throw new InvalidOperationException("环境变量 COMMAND_CODE_API_KEY 覆盖登录配置，请清除后再切换；其他终端也需要检查");
        busy = true; Render();
        try { string key = Core.Decrypt(a.Secret); AccountData data = await Core.Fetch(key); if (a.Data == null || data.UserId != a.Data.UserId || data.OrgId != a.Data.OrgId) throw new InvalidOperationException("账号身份变化，已停止切换"); Core.Switch(authFile, key, data); a.Data = data; a.Error = null; Save(); }
        finally { busy = false; Render(); }
        status.Text = "已切换为 " + a.Name + "；新会话生效，已运行的 CommandCode 会话请重新打开。";
    }
    private void Remove() { if (busy) throw new InvalidOperationException("正在同步，请稍后移除"); Account a = Selected(); if (a == null) return; if (MessageBox.Show(this, "从管理列表移除“" + a.Name + "”？本机登录保持原样。", "移除账号", MessageBoxButtons.OKCancel) == DialogResult.OK) { store.Accounts.Remove(a); Save(); Schedule(); Render(); } }
    private async Task AddDialog() {
        if (busy || login != null) throw new InvalidOperationException("请先完成当前操作");
        using (Form popup = new Form { Text = "添加账号", ClientSize = new Size(440, 268), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, Font = Font }) {
            Label nameLabel = new Label { Text = "账号备注（可选）", Location = new Point(20, 20), AutoSize = true }; TextBox name = new TextBox { Location = new Point(20, 46), Width = 400 };
            Label keyLabel = new Label { Text = "API Key（选择密码登录时无需填写）", Location = new Point(20, 82), AutoSize = true }; TextBox key = new TextBox { Location = new Point(20, 108), Width = 400, UseSystemPasswordChar = true };
            Label hint = new Label { Text = "密码登录将打开官方网页，输入邮箱密码并授权。\n凭据自动添加，客户端不保存密码。", Location = new Point(20, 148), Size = new Size(400, 48), ForeColor = Color.SlateGray };
            string choice = ""; Button password = new ModernButton { Primary = true, Text = "账号密码登录", Location = new Point(20, 212), Size = new Size(185, 34) }, api = new ModernButton { Text = "验证并添加 API Key", Location = new Point(220, 212), Size = new Size(200, 34) }; password.Click += (s, e) => { choice = "password"; popup.DialogResult = DialogResult.OK; }; api.Click += (s, e) => { if (string.IsNullOrWhiteSpace(key.Text)) { hint.Text = "请填写 API Key"; return; } choice = "api"; popup.DialogResult = DialogResult.OK; };
            popup.Controls.AddRange(new Control[] { nameLabel, name, keyLabel, key, hint, password, api }); if (popup.ShowDialog(this) != DialogResult.OK) return;
            if (choice == "api") { await Add(key.Text, name.Text.Trim(), "api"); return; }
            using (login = new LoginFlow()) {
                Form waiting = new Form { Text = "等待官方登录", ClientSize = new Size(430, 135), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, Font = Font, MaximizeBox = false, MinimizeBox = false };
                waiting.Controls.Add(new Label { Text = "请在官方网页输入邮箱密码并授权。\n添加其他账号时，请在官方网页选择对应账号。", Location = new Point(18, 18), Size = new Size(395, 55) }); Button cancel = new ModernButton { Text = "取消登录", Location = new Point(280, 87), Size = new Size(126, 30) }; cancel.Click += (s, e) => waiting.Close(); waiting.Controls.Add(cancel); waiting.FormClosed += (s, e) => { if (login != null) login.Dispose(); }; waiting.Show(this);
                try { Task<string> result = login.Wait(); Process.Start(new ProcessStartInfo(login.Url) { UseShellExecute = true }); string token = await result; waiting.Hide(); await Add(token, name.Text.Trim(), "password"); }
                finally { waiting.Dispose(); login = null; }
            }
        }
    }
    private void SettingsDialog() {
        using (Form popup = new Form { Text = "监控设置", ClientSize = new Size(395, 300), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Color.White }) {
            string[] labels = { "刷新间隔（秒）", "预警比例（%）", "严重预警（%）", "低余额（美元）" }; decimal[] values = { store.Settings.Interval, store.Settings.Warning, store.Settings.Critical, (decimal)store.Settings.LowBalance }; NumericUpDown[] fields = new NumericUpDown[4];
            for (int i = 0; i < 4; i++) { popup.Controls.Add(new Label { Text = labels[i], AutoSize = true, Location = new Point(20, 25 + i * 42) }); fields[i] = new NumericUpDown { Location = new Point(220, 20 + i * 42), Width = 145, Minimum = i == 0 ? 60 : i == 3 ? 0 : 1, Maximum = i == 0 ? 3600 : i == 3 ? 100000 : 100, DecimalPlaces = i == 3 ? 2 : 0, Value = values[i] }; popup.Controls.Add(fields[i]); }
            CheckBox notifications = new CheckBox { Text = "开启桌面提醒", Checked = store.Settings.Notifications, Location = new Point(20, 194), AutoSize = true }; popup.Controls.Add(notifications); popup.Controls.Add(new Label { Text = "套餐周期提前 7 / 3 / 1 天提醒，相同提醒不重复弹出。", Location = new Point(20, 224), Size = new Size(355, 23), ForeColor = Color.SlateGray });
            Button save = new ModernButton { Primary = true, Text = "保存", Location = new Point(255, 258), Size = new Size(110, 30) }; save.Click += (s, e) => { if (fields[1].Value >= fields[2].Value) { MessageBox.Show(popup, "预警比例必须小于严重预警比例"); return; } popup.DialogResult = DialogResult.OK; }; popup.Controls.Add(save);
            if (popup.ShowDialog(this) == DialogResult.OK) { store.Settings.Interval = (int)fields[0].Value; store.Settings.Warning = (int)fields[1].Value; store.Settings.Critical = (int)fields[2].Value; store.Settings.LowBalance = (double)fields[3].Value; store.Settings.Notifications = notifications.Checked; Save(); Schedule(); Render(); }
        }
    }
    public void SeedTest(int count) {
        for (int i = 0; i < count; i++) store.Accounts.Add(new Account { Id = i.ToString(), Name = "界面测试账号 " + (i + 1), Secret = Core.Encrypt("fixture-key-" + i), Method = i % 2 == 0 ? "password" : "api", Data = new AccountData { UserId = i.ToString(), Email = "demo" + i + "@example.com", Plan = "pro", Status = "active", Remaining = 32.80, Monthly = 28, Purchased = 4.8, Free = 0, Limited = true, PeriodEnd = "2030-10-30T08:00:00Z", CancelAtPeriodEnd = false, FiveHour = new WindowUsage { Used = 9.92, Cap = 16, ResetAt = "2030-10-13T12:00:00Z" }, Weekly = new WindowUsage { Used = 34, Cap = 40, ResetAt = "2030-10-19T08:00:00Z" } } });
    }
    public bool CheckPaging() { page = 0; Render(); int first = grid.Rows.Count; bool nextEnabled = next.Enabled; if (!nextEnabled) return false; next.PerformClick(); return first > 0 && grid.Rows.Count > 0 && page == 1 && grid.ScrollBars == ScrollBars.None && grid.Rows.Cast<DataGridViewRow>().All(row => row.Displayed); }
    public bool CheckDragAndPager() { Point before = Location; BeginDrag(new Point(100, 100)); MoveDrag(new Point(150, 125)); bool moved = Location == new Point(before.X + 50, before.Y + 25); dragging = false; Location = before; Point pager = next.PointToScreen(Point.Empty); return moved && pager.X > PointToScreen(new Point(ClientSize.Width / 2, 0)).X; }
}
public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "commandcode-account-manager"); string file = Path.Combine(root, "accounts-native.json");
        if (args.Length > 0 && args[0] == "--self-test") { SelfTest.Run(args.Length > 1 ? args[1] : Environment.CurrentDirectory); return; }
        if (args.Length > 1 && args[0] == "--measure") { SelfTest.Measure(args[1]); return; }
        bool owner; using (Mutex mutex = new Mutex(true, "Local\\CommandCodeNativeManager", out owner)) { if (!owner) { MessageBox.Show("客户端已在运行，请从托盘打开。"); return; } try { Application.Run(new ManagerForm(file, false)); } catch (Exception e) { MessageBox.Show("无法打开客户端：" + e.Message + "。原账号数据已保留。", "CommandCode"); } }
    }
}
public static class SelfTest {
    private static void Assert(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
    public static void Measure(string output) {
        string temp = Path.Combine(Path.GetTempPath(), "commandcode-measure-" + Guid.NewGuid().ToString("N"));
        using (ManagerForm form = new ManagerForm(Path.Combine(temp, "store.json"), true)) {
            form.SeedTest(24); System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 3000 }; int stage = 0; TimeSpan cpuStart = TimeSpan.Zero; DateTime start = DateTime.UtcNow; long foreground = 0;
            timer.Tick += (s, e) => {
                using (Process process = Process.GetCurrentProcess()) {
                    process.Refresh();
                    if (stage == 0) { foreground = process.WorkingSet64; form.Hide(); cpuStart = process.TotalProcessorTime; start = DateTime.UtcNow; timer.Interval = 15000; stage = 1; }
                    else { double cpu = (process.TotalProcessorTime - cpuStart).TotalSeconds / (DateTime.UtcNow - start).TotalSeconds / Environment.ProcessorCount * 100; Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "native-resource-result.json"), Core.Json.Serialize(new { foregroundMiB = foreground / 1048576.0, backgroundMiB = process.WorkingSet64 / 1048576.0, cpuPercent = cpu, elapsedSeconds = (DateTime.UtcNow - start).TotalSeconds, accounts = 24, networkRefresh = false })); timer.Stop(); form.Close(); }
                }
            };
            form.Shown += (s, e) => timer.Start(); Application.Run(form); timer.Dispose();
        }
    }
    public static void Run(string output) {
        Directory.CreateDirectory(output); string temp = Path.Combine(Path.GetTempPath(), "commandcode-native-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try {
            Assert(Core.Decrypt(Core.Encrypt("fake-test-key")) == "fake-test-key", "DPAPI 加密回读");
            Dictionary<string, object> empty = new Dictionary<string, object>(); Assert(!Core.Normalize(empty, empty, empty).Remaining.HasValue, "未获取额度保持未知");
            string auth = Path.Combine(temp, "auth.json"); Core.AtomicWrite(auth, "{\"apiKey\":\"old-fake\",\"otherProvider\":{\"token\":\"keep\"}}"); Core.Switch(auth, "new-fake", new AccountData { UserId = "u1", UserName = "test" }); Dictionary<string, object> after = Core.Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(auth)); Assert(Core.Text(after, "apiKey") == "new-fake" && Core.Text(Core.Object(Core.Value(after, "otherProvider")), "token") == "keep", "切换保留其他凭据"); string backup = Directory.GetFiles(temp, "*.encrypted").Single(); Assert(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(backup), null, DataProtectionScope.CurrentUser)).Contains("old-fake"), "原配置备份可恢复");
            Account sample = new Account { Id = "a1", Data = new AccountData { Limited = true, Remaining = 3, FiveHour = new WindowUsage { Used = 96, Cap = 100, ResetAt = "2030-01-01T00:00:00Z" } } }; Assert(Core.Alerts(sample, new Settings(), DateTimeOffset.Parse("2026-10-13T00:00:00Z")).Count == 2, "额度与余额预警"); sample.Data.FiveHour.ResetAt = "2020-01-01T00:00:00Z"; Assert(Core.Alerts(sample, new Settings(), DateTimeOffset.UtcNow).Count == 1, "已结束窗口不预警");
            using (ManagerForm form = new ManagerForm(Path.Combine(temp, "store.json"), true)) {
                form.SeedTest(24); form.Show(); Application.DoEvents(); Assert(form.CheckPaging(), "24 账号分页且行完全可见");
                Assert(form.CheckDragAndPager(), "拖动移动窗口、分页位于右侧");
                using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(output, "native-preview.png")); }
                form.ClientSize = new Size(984, 580); Application.DoEvents(); Assert(form.CheckPaging(), "小窗口分页不溢出"); form.Close();
            }
            using (LoginFlow flow = new LoginFlow()) {
                Task<string> callback = Task.Run(() => flow.Wait()); Uri login = new Uri(flow.Url); Dictionary<string, string> query = login.Query.TrimStart('?').Split('&').Select(part => part.Split(new char[] { '=' }, 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
                Task test = Task.Run(async () => { using (HttpClient http = new HttpClient()) { Dictionary<string, string> fields = new Dictionary<string, string> { { "state", "wrong" }, { "apiKey", "fake" }, { "userId", "u1" }, { "userName", "test" }, { "keyName", "cli" } }; http.DefaultRequestHeaders.Add("Origin", "null"); HttpResponseMessage bad = await http.PostAsync(query["callback"], new FormUrlEncodedContent(fields)); Assert((int)bad.StatusCode == 403, "null 来源依然校验随机 state"); fields["state"] = query["state"]; http.DefaultRequestHeaders.Remove("Origin"); http.DefaultRequestHeaders.Add("Origin", "https://example.com"); HttpResponseMessage foreign = await http.PostAsync(query["callback"], new FormUrlEncodedContent(fields)); Assert((int)foreign.StatusCode == 403, "拒绝第三方来源"); http.DefaultRequestHeaders.Remove("Origin"); http.DefaultRequestHeaders.Add("Origin", "null"); HttpResponseMessage good = await http.PostAsync(query["callback"], new FormUrlEncodedContent(fields)); Assert(good.IsSuccessStatusCode, "修复真实浏览器 null 来源表单 Forbidden"); } });
                test.GetAwaiter().GetResult(); Assert(callback.GetAwaiter().GetResult() == "fake", "回调凭据接收");
            }
            File.WriteAllText(Path.Combine(output, "native-test-result.json"), "{\"passed\":true,\"nullOriginCallback\":true,\"invalidStateRejected\":true,\"foreignOriginRejected\":true,\"pagingFits\":true,\"dragMovesWindow\":true,\"pagerOnRight\":true}");
        } catch (Exception e) { File.WriteAllText(Path.Combine(output, "native-test-result.json"), Core.Json.Serialize(new { passed = false, error = e.Message })); }
    }
}
}
