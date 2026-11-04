using System;
using System.IO;
using ModernHost;
public static class ProxyLogChecks {
    public static void Run(string temp) {
        Directory.CreateDirectory(temp);
        string file = Path.Combine(temp, "proxy-requests.jsonl");
        for (int i = 0; i < 520; i++) ProxyRequestLog.Append(file, new ProxyRecord { At = "2026-11-01T02:00:00Z", Account = "account-" + i, Model = "model-a", Endpoint = "/v1/chat/completions", Status = 200, DurationMs = 12, Input = 10, Output = 5, Note = "完成" });
        ProxyRecord[] rows = ProxyRequestLog.Load(file, 40);
        if (rows.Length != 40) throw new Exception("代理记录读取条数不正确：" + rows.Length);
        if (rows[rows.Length - 1].Account != "account-519") throw new Exception("代理记录顺序不正确");
        if (rows[0].Input != 10 || rows[0].Output != 5) throw new Exception("代理记录 Token 丢失");
        string csv = ProxyRequestLog.Csv(rows);
        string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (!lines[0].StartsWith("\uFEFF时间,账号,模型,接口")) throw new Exception("代理记录 CSV 表头不正确");
        if (lines.Length != 41) throw new Exception("代理记录 CSV 行数不正确：" + lines.Length);
        if (!csv.Contains("account-519")) throw new Exception("代理记录 CSV 内容缺失");
        if (ProxyRequestLog.Load(Path.Combine(temp, "missing.jsonl"), 10).Length != 0) throw new Exception("缺失文件应返回空");
        Console.WriteLine("PASS 代理请求记录：追加、滚动读取、CSV 导出、缺失文件");
    }
}
