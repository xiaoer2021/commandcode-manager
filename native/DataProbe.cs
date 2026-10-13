using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommandCodeManager;
public class DataProbe {
    static Dictionary<string, object> Shape(Dictionary<string, object> data) { Dictionary<string, object> result = new Dictionary<string, object>(); foreach (KeyValuePair<string, object> pair in data) { Dictionary<string, object> nested = pair.Value as Dictionary<string, object>; result[pair.Key] = nested == null ? pair.Value == null ? "null" : pair.Value.GetType().Name : (object)Shape(nested); } return result; }
    public static void Main(string[] args) { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; Run().GetAwaiter().GetResult(); }
    static async Task Run() {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "commandcode-account-manager", "accounts-native.json"); Store store = Core.Json.Deserialize<Store>(File.ReadAllText(file)); int index = 0;
        foreach (Account account in store.Accounts) {
            string key = Core.Decrypt(account.Secret); string query = account.Data == null || string.IsNullOrEmpty(account.Data.OrgId) ? "" : "orgId=" + Uri.EscapeDataString(account.Data.OrgId);
            Dictionary<string, object> credits = await Core.Get(key, "/alpha/billing/credits", query); Dictionary<string, object> subscription = await Core.Get(key, "/alpha/billing/subscriptions", query); Dictionary<string, object> sub = Core.Object(Core.Value(subscription, "data")); string since = Core.Text(sub, "currentPeriodStart");
            Dictionary<string, object> summary = string.IsNullOrEmpty(since) ? new Dictionary<string, object>() : await Core.Get(key, "/alpha/usage/summary", query + (query == "" ? "" : "&") + "since=" + Uri.EscapeDataString(since));
            Console.WriteLine(Core.Json.Serialize(new { accountIndex = index++, creditsShape = Shape(credits), subscriptionShape = Shape(subscription), summaryShape = Shape(summary), periodStart = since, periodEnd = Core.Text(sub, "currentPeriodEnd"), summaryCost = Core.Number(summary, "totalCost"), monthlyRemaining = Core.Number(Core.Object(Core.Value(credits, "credits")), "monthlyCredits") }));
        }
    }
}
