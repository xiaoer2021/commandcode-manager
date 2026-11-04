using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using CommandCodeManager;
namespace ModernHost {
public static class ProxyRequestLog {
    private static readonly object Gate = new object();
    public static ProxyRecord[] Load(string path, int take) {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new ProxyRecord[0];
        List<ProxyRecord> rows = new List<ProxyRecord>();
        try { foreach (string line in File.ReadAllLines(path)) { if (string.IsNullOrWhiteSpace(line)) continue; try { ProxyRecord record = new JavaScriptSerializer().Deserialize<ProxyRecord>(line); if (record != null) rows.Add(record); } catch { } } } catch { }
        return rows.Skip(Math.Max(0, rows.Count - take)).ToArray();
    }
    public static void Append(string path, ProxyRecord record) {
        if (string.IsNullOrEmpty(path) || record == null) return;
        lock (Gate) {
            if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) {
                StringBuilder trimmed = new StringBuilder();
                foreach (ProxyRecord row in Load(path, 500)) trimmed.AppendLine(new JavaScriptSerializer().Serialize(row));
                Core.AtomicWrite(path, trimmed.ToString());
            }
            File.AppendAllText(path, new JavaScriptSerializer().Serialize(record) + "\n", new UTF8Encoding(false));
        }
    }
    public static string Csv(ProxyRecord[] rows) {
        StringBuilder text = new StringBuilder("\uFEFF时间,账号,模型,接口,状态,耗时（毫秒）,输入Token,输出Token,说明\r\n");
        foreach (ProxyRecord row in rows ?? new ProxyRecord[0]) text.AppendLine(string.Join(",", new[] { UsageCsv.Cell(Time(row.At)), UsageCsv.Cell(row.Account), UsageCsv.Cell(row.Model), UsageCsv.Cell(row.Endpoint), row.Status.ToString(), row.DurationMs.ToString(), row.Input.HasValue ? row.Input.Value.ToString("0") : "", row.Output.HasValue ? row.Output.Value.ToString("0") : "", UsageCsv.Cell(row.Note) }));
        return text.ToString();
    }
    public static string Time(string value) { DateTimeOffset time; return Core.TryTime(value, out time) ? time.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss") : value; }
}
}
