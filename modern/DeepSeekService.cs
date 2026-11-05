using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CommandCodeManager;
namespace ModernHost {
// DeepSeek 作为完全独立的顶层平台（Platform="deepseek"）。
// 与 commandcode / opencode 并列，走 OpenAI 兼容接口：余额来自 /user/balance，Token 历史由本地代理日志累积。
public sealed class DeepSeekService {
    private static readonly HttpClient remote = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public static bool IsDeepSeek(Account account) { return account != null && account.Platform == "deepseek"; }
    public static string ProviderName { get { return "DeepSeek"; } }
    public static string PlatformName { get { return "DeepSeek"; } }

    // 余额接口：DeepSeek 返回 USD 余额，无按账号/组织的身份，凭据去重仅靠 API Key。
    public static async Task<AccountData> Fetch(string key) {
        AccountData data = new AccountData {
            UserId = "", OrgId = "", UserName = "DeepSeek 账号", Email = "",
            Plan = "deepseek-api", Status = "active", FetchedAt = DateTimeOffset.UtcNow.ToString("o"),
            UsageUnit = "usd", UsageSource = "DeepSeek 余额接口", Limited = null, Remaining = null
        };
        try {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance")) {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.UserAgent.ParseAdd("CommandCodeManager/1.0");
                using (var response = await remote.SendAsync(request)) {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("DeepSeek 余额接口返回 HTTP " + (int)response.StatusCode);
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                    var balances = Core.Value(root, "balance_infos") as System.Collections.IEnumerable;
                    if (balances != null) {
                        foreach (object balance in balances) {
                            var item = Core.Object(balance);
                            if (Core.Text(item, "currency") == "USD") {
                                double amount;
                                if (double.TryParse(Core.Text(item, "total_balance"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount))
                                    data.Remaining = amount;
                            }
                        }
                    }
                }
            }
        } catch (Exception error) { data.Error = "DeepSeek 余额获取失败：" + error.Message; }
        return data;
    }

    // Token 用量历史：DeepSeek API 本身不返回按日/周/月明细，因此累积本地代理请求日志。
    public static HistoryWindow History(string accountId, string accountName, HistoryQuery query, string logPath) {
        int pages; DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset[] boundaries = TokenHistoryCache.Dates(query, now, out pages);
        ProxyRecord[] records = new ProxyRecord[0];
        try { records = ProxyRequestLog.Load(logPath, int.MaxValue).Where(r => r != null && r.Account == accountName).ToArray(); } catch { }
        var rows = new List<TokenPeriod>();
        for (int i = boundaries.Length - 2; i >= 0; i--) {
            long from = (long)(boundaries[i].ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMilliseconds;
            long to = (long)(boundaries[i + 1].ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMilliseconds;
            var values = records.Where(r => {
                DateTimeOffset at; if (!Core.TryTime(r.At, out at)) return false;
                long ms = at.ToUniversalTime().ToUnixTimeMilliseconds();
                return ms >= from && ms < to;
            }).ToArray();
            rows.Add(new TokenPeriod {
                Start = boundaries[i].ToString("o"),
                End = (boundaries[i + 1] > now ? now : boundaries[i + 1]).ToString("o"),
                Tokens = values.Sum(r => (r.Input ?? 0) + (r.Output ?? 0)),
                Input = values.Sum(r => r.Input ?? 0),
                Output = values.Sum(r => r.Output ?? 0),
                Requests = values.Length,
                Cost = null
            });
        }
        return new HistoryWindow {
            AccountId = accountId, Grain = query.Grain, Page = query.Page, Start = query.Start, End = query.End,
            Pages = pages, HasNext = query.Page + 1 < pages, Rows = rows.ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o"), Complete = true
        };
    }
}
}
