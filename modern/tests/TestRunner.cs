using System;
using System.IO;
using System.Linq;
using ModernHost;
using CommandCodeManager;

public static class TestRunner {
    static int failures = 0;
    public static int Main(string[] args) {
        string tmp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Run("CoreChecks", () => CoreChecks.Main(new string[0]));
        Run("HistoryCacheChecks", () => Console.WriteLine(HistoryCacheChecks.Run(tmp)));
        Run("LocalProxyChecks", () => Console.WriteLine(LocalProxyChecks.Run()));
        Run("OpenCodeChecks", () => Console.WriteLine(OpenCodeChecks.Run(tmp)));
        Run("OpenCodeProxyChecks", () => { OpenCodeProxyChecks.Run(); Console.WriteLine("PASS"); });
        Run("PortableFeatureChecks", () => Console.WriteLine(PortableFeatureChecks.Run(tmp)));
        Run("ProxyLogChecks", () => { ProxyLogChecks.Run(tmp); Console.WriteLine("PASS"); });
        Run("ProxyNetworkChecks", () => { ProxyNetworkChecks.Run(); Console.WriteLine("PASS"); });
        Run("ProxyWorkspaceChecks", () => { ProxyWorkspaceChecks.Run(); });
        Run("ProxyQuotaChecks", () => { ProxyQuotaChecks.Run(); Console.WriteLine("PASS"); });
        Run("DeepSeekChecks", () => Console.WriteLine(DeepSeekChecks.Run(tmp)));
        Console.WriteLine(failures == 0 ? "=== ALL TESTS PASSED ===" : ("=== " + failures + " TEST(S) FAILED ==="));
        try { Directory.Delete(tmp, true); } catch { }
        return failures;
    }
    static void Run(string name, Action action) {
        try { action(); Console.WriteLine("[PASS] " + name); }
        catch (Exception e) { failures++; Console.WriteLine("[FAIL] " + name + ": " + e.Message); }
    }
}

public static class DeepSeekChecks {
    static void Check(bool v, string m) { if (!v) throw new Exception(m); }
    public static string Run(string tmp) {
        Check(DeepSeekService.IsDeepSeek(new Account { Platform = "deepseek" }), "IsDeepSeek 未识别 deepseek");
        Check(!DeepSeekService.IsDeepSeek(new Account { Platform = "commandcode" }), "commandcode 被误判为 deepseek");
        Check(!DeepSeekService.IsDeepSeek(new Account { Platform = "opencode" }), "opencode 被误判为 deepseek");
        Check(!DeepSeekService.IsDeepSeek(null), "null 被误判为 deepseek");
        Check(OpenCodeService.ProviderName("deepseek") == "DeepSeek", "ProviderName(deepseek) 未返回 DeepSeek");
        Check(Core.PlanLabel("deepseek-api") == "DeepSeek API", "PlanLabel(deepseek-*) 未返回 DeepSeek API");

        PortableAccount[] ds = { new PortableAccount { Name = "DeepSeek 测试", Platform = "deepseek", ApiKey = "ds-fixture-key", Method = "api" } };
        string enc = AccountTransfer.Encrypt(ds, "fixture-password");
        PortableAccount[] restored = AccountTransfer.Decrypt(enc, "fixture-password");
        Check(restored[0].Platform == "deepseek" && restored[0].ApiKey == "ds-fixture-key", "deepseek 可移植往返失败");

        Account account = new Account { Name = "ds", Platform = "deepseek" };
        TokenPeriod[] rows = { new TokenPeriod { Start = "2026-11-05T00:00:00Z", End = "2026-11-06T00:00:00Z", Tokens = 15, Input = 10, Output = 5, Requests = 1 } };
        string csv = UsageCsv.Create(account, rows);
        Check(csv.Contains("本地代理日志累积"), "UsageCsv deepseek 来源标签错误");

        string log = Path.Combine(tmp, "deepseek-proxy.jsonl");
        ProxyRequestLog.Append(log, new ProxyRecord { At = DateTimeOffset.UtcNow.ToString("o"), Account = "ds-account", Model = "deepseek-chat", Endpoint = "/v1/chat/completions", Status = 200, DurationMs = 10, Input = 10, Output = 5, Note = "ok" });
        HistoryWindow h = DeepSeekService.History("ds-id", "ds-account", new HistoryQuery { Grain = "day", Page = 0 }, log);
        Check(h.Complete, "deepseek 历史未完成");
        Check(h.Rows.Length == 7, "deepseek day 分桶数应为 7，实际 " + h.Rows.Length);
        Check(h.Rows.Sum(r => (r.Input ?? 0) + (r.Output ?? 0)) == 15, "deepseek token 聚合错误");
        Check(h.Rows.Sum(r => r.Requests ?? 0) == 1, "deepseek 请求计数错误");
        return "PASS deepseek: 平台识别、ProviderName、PlanLabel、可移植往返、CSV 来源标签、代理日志 Token 聚合";
    }
}
