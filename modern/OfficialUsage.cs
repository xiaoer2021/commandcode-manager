using System;
using System.IO;
using System.Text.RegularExpressions;
using CommandCodeManager;
namespace ModernHost {
public static class OfficialUsage {
    public static string Renderer { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Command Code", "resources", "app", "out", "renderer"); } }
    private const string Marker = "data-cmd-manager-credits=\"1\"";
    private static object cachedStatus;
    private static DateTime statusAt;
    public static object Status() {
        if (cachedStatus != null && (DateTime.UtcNow - statusAt).TotalSeconds < 5) return cachedStatus;
        string index = Path.Combine(Renderer, "index.html"); bool available = File.Exists(index); string html = available ? File.ReadAllText(index) : "";
        statusAt = DateTime.UtcNow;
        return cachedStatus = new { available = available, installed = html.Contains(Marker), source = "内置用量面板", requiresInitialReload = true };
    }
    public static bool IsInstalled() { string index = Path.Combine(Renderer, "index.html"); return File.Exists(index) && File.ReadAllText(index).Contains(Marker); }
    public static void Apply(bool enabled, string bundled, bool? activeEnabled = null, string userId = "") { ApplyAt(Renderer, enabled, bundled, activeEnabled, userId); }
    public static void WriteState(bool enabled, string userId) {
        string config = Path.Combine(Renderer, "assets", "manager-credits-state.js");
        if (File.Exists(config)) Core.AtomicWrite(config, Config(enabled, userId));
    }
    private static string Config(bool enabled, string userId) { return "window.__cmdManagerCreditsEnabled=" + (enabled ? "true" : "false") + ";window.__cmdManagerCreditsUser=" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(userId) + ";"; }
    public static void ApplyAt(string renderer, bool enabled, string bundled, bool? activeEnabled = null, string userId = "") {
        string index = Path.Combine(renderer, "index.html");
        if (!File.Exists(index)) { if (enabled) throw new InvalidOperationException("未找到 Command Code 官方客户端，请先安装客户端"); return; }
        string before = File.ReadAllText(index); if (!before.Contains("</body>")) throw new InvalidOperationException("官方客户端页面结构异常，已保留原文件");
        if (enabled) foreach (string name in new string[] { "manager-credits-loader.js", "manager-credits-panel.js" }) if (!File.Exists(Path.Combine(bundled, name))) throw new InvalidOperationException("内置用量组件缺失，未启用");
        string assets = Path.Combine(renderer, "assets"); Directory.CreateDirectory(assets);
        string html = Regex.Replace(before, @"\s*<script\b[^>]*data-cmd-(?:manager-credits|credits)\s*=\s*[""']1[""'][^>]*>\s*</script>", "", RegexOptions.IgnoreCase);
        string backup = index + ".manager-credits.bak"; if (!File.Exists(backup) && (html != before || enabled)) File.Copy(index, backup, false);
        if (enabled) {
            foreach (string name in new string[] { "manager-credits-loader.js", "manager-credits-panel.js" }) { string source = Path.Combine(bundled, name); if (!File.Exists(source)) throw new InvalidOperationException("内置用量组件缺失，未启用"); File.Copy(source, Path.Combine(assets, name), true); }
            html = html.Replace("</body>", "<script src=\"./assets/manager-credits-loader.js\" " + Marker + "></script>\n</body>");
        }
        string config = Path.Combine(assets, "manager-credits-state.js"); bool hadConfig = File.Exists(config); string oldConfig = hadConfig ? File.ReadAllText(config) : "window.__cmdManagerCreditsEnabled=false;";
        try {
            Core.AtomicWrite(config, Config(activeEnabled ?? enabled, userId));
            Core.AtomicWrite(index, html); if (File.ReadAllText(index).Contains(Marker) != enabled) throw new InvalidOperationException("用量面板安装回读失败");
            cachedStatus = null;
        }
        catch { Core.AtomicWrite(index, before); Core.AtomicWrite(config, oldConfig); throw; }
    }
}
}
