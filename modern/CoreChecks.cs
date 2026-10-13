using System;
using System.IO;
using System.Linq;
using CommandCodeManager;
public static class CoreChecks {
    public static void Main(string[] args) {
        DateTimeOffset time; if (!Core.TryTime("1791891415497", out time) || Core.NormalizeTime("1791891415497").Contains("1791891415497")) throw new Exception("毫秒时间戳未转换");
        DateTimeOffset second; if (!Core.TryTime("1791891415", out second) || Math.Abs((time-second).TotalSeconds)>1) throw new Exception("秒/毫秒时间戳不一致");
        if (Core.TryTime("0",out time) || !Core.Date("0").Contains("首次使用")) throw new Exception("零时间戳不应显示未获取");
        if (Core.PlanLabel("individual-goat")!="GOAT · 个人版" || Core.StatusLabel("active")!="有效") throw new Exception("套餐/状态未清洗");
        AccountData monthly = new AccountData { Plan = "individual-goat", Status = "active", Monthly = 56.23, PeriodCost = 9.86 }; Core.ProjectMonthly(monthly); if (monthly.MonthlyTotal != 70 || Math.Abs(monthly.MonthlyUsed.Value - 13.77)>0.00001 || monthly.PeriodCost != 9.86) throw new Exception("额度消耗不能与请求费用混用");
        if (Core.Decrypt(Core.Encrypt("fake-secret"))!="fake-secret") throw new Exception("加密回读失败");
        Console.WriteLine("时间戳、未开始窗口、套餐清洗、状态清洗、加密回读通过");
    }
}
