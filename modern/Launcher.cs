using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace FixedLauncher {
public static class Program {
    [STAThread]
    public static void Main(string[] args) {
        string root = AppDomain.CurrentDomain.BaseDirectory; string app = Path.Combine(root, "app");
        try {
            bool restart;
            do {
                AppDomain domain = AppDomain.CreateDomain("CommandCodeApp", null, new AppDomainSetup { ApplicationBase = app, ShadowCopyFiles = "true", CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommandCodeManager", "assembly-cache") });
                try { FixedContract.IRunner runner = (FixedContract.IRunner)domain.CreateInstanceFromAndUnwrap(Path.Combine(app, "Host.dll"), "ModernHost.DomainRunner"); restart = runner.Run(args); }
                finally { AppDomain.Unload(domain); }
            } while (restart);
        } catch (Exception e) { File.WriteAllText(Path.Combine(root, "startup-error.txt"), e.ToString()); if (Array.IndexOf(args, "--verify") < 0 && Array.IndexOf(args, "--verify-live") < 0) MessageBox.Show("客户端启动失败：" + e.Message, "CommandCode Manager"); }
    }
}
}
