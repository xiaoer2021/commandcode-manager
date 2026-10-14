using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommandCodeManager;
namespace ModernHost {
public class TokenPeriod {
    public double? CacheRead { get; set; }
    public double? CacheWrite { get; set; }
    public double? Reasoning { get; set; }
    public string Start { get; set; }
    public string End { get; set; }
    public double? Tokens { get; set; }
    public double? Input { get; set; }
    public double? Output { get; set; }
    public double? Requests { get; set; }
    public double? Cost { get; set; }
    public string Error { get; set; }
}
public static class TokenHistory {
    private static readonly SemaphoreSlim Slots = new SemaphoreSlim(2);
    public static async Task<Dictionary<string,object>> Snapshot(string key,string org,DateTimeOffset date,DateTimeOffset now) {
        if(date>=now) return new Dictionary<string,object>{{"totalTokens",0},{"totalTokensIn",0},{"totalTokensOut",0},{"totalCount",0},{"totalCost",0}};
        string prefix=string.IsNullOrEmpty(org)?"":"orgId="+Uri.EscapeDataString(org)+"&";
        await Slots.WaitAsync();
        try { return await Core.Get(key,"/alpha/usage/summary",prefix+"since="+Uri.EscapeDataString(date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))); }
        finally { Slots.Release(); }
    }
    public static double? Difference(Dictionary<string,object> older, Dictionary<string,object> newer, string field) {
        double? a=Core.Number(older,field), b=Core.Number(newer,field);
        return a.HasValue && b.HasValue && a.Value>=b.Value ? (double?)(a.Value-b.Value) : null;
    }
    public static DateTimeOffset[] Boundaries(string grain, int page, DateTimeOffset now) {
        if (grain!="day" && grain!="week" && grain!="month" || page<0 || page>60) throw new InvalidOperationException("统计周期无效");
        DateTimeOffset local=now.ToOffset(TimeSpan.FromHours(8));
        DateTimeOffset end=new DateTimeOffset(local.Year,local.Month,local.Day,0,0,0,local.Offset);
        int count=grain=="day"?7:6;
        if(grain=="month") end=new DateTimeOffset(local.Year,local.Month,1,0,0,0,local.Offset).AddMonths(1-page*count);
        else if(grain=="week") end=end.AddDays(-((int)local.DayOfWeek+6)%7+7-page*count*7);
        else end=end.AddDays(1-page*count);
        return Enumerable.Range(0,count+1).Select(i=>grain=="month"?end.AddMonths(i-count):end.AddDays((i-count)*(grain=="week"?7:1))).ToArray();
    }
    public static DateTimeOffset[] RangeBoundaries(string grain,string start,string end,int page,DateTimeOffset now,out int pages) {
        if(grain!="day"&&grain!="week"&&grain!="month"||page<0)throw new InvalidOperationException("统计周期无效");
        DateTime first,last;DateTime today=now.ToOffset(TimeSpan.FromHours(8)).Date;
        if(!DateTime.TryParseExact(start,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out first)||!DateTime.TryParseExact(end,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out last)||first>last||last>today||(last-first).TotalDays>365)throw new InvalidOperationException("请选择有效日期，范围最多一年，结束日期不能晚于今天");
        DateTimeOffset cursor=new DateTimeOffset(first,TimeSpan.FromHours(8)),stop=new DateTimeOffset(last.AddDays(1),TimeSpan.FromHours(8));List<DateTimeOffset> all=new List<DateTimeOffset>{cursor};
        while(cursor<stop) {DateTimeOffset next=grain=="day"?cursor.AddDays(1):grain=="week"?cursor.AddDays(7-((int)cursor.DayOfWeek+6)%7):new DateTimeOffset(cursor.Year,cursor.Month,1,0,0,0,cursor.Offset).AddMonths(1);cursor=next<stop?next:stop;all.Add(cursor);}
        int count=grain=="day"?7:6,segments=all.Count-1;pages=(segments+count-1)/count;if(page>=pages)throw new InvalidOperationException("已到最早记录");int lastIndex=segments-page*count,firstIndex=Math.Max(0,lastIndex-count);return all.Skip(firstIndex).Take(lastIndex-firstIndex+1).ToArray();
    }
    public static async Task<TokenPeriod[]> Fetch(string key,string org,string grain,int page) {
        DateTimeOffset now=DateTimeOffset.UtcNow; DateTimeOffset[] dates=Boundaries(grain,page,now);
        string prefix=string.IsNullOrEmpty(org)?"":"orgId="+Uri.EscapeDataString(org)+"&";
        Dictionary<string,object>[] values=new Dictionary<string,object>[dates.Length]; string[] errors=new string[dates.Length];
        await Task.WhenAll(dates.Select(async (date,index)=>{
            if(date>=now) { values[index]=new Dictionary<string,object>{{"totalTokens",0},{"totalTokensIn",0},{"totalTokensOut",0},{"totalCount",0},{"totalCost",0}}; return; }
            await Slots.WaitAsync();
            try { values[index]=await Core.Get(key,"/alpha/usage/summary",prefix+"since="+Uri.EscapeDataString(date.ToUniversalTime().ToString("yyyy-MM-dd\u0027T\u0027HH:mm:ss\u0027Z\u0027"))); }
            catch(Exception e) { errors[index]=e.Message; values[index]=new Dictionary<string,object>(); }
            finally { Slots.Release(); }
        }));
        return Enumerable.Range(0,dates.Length-1).Select(i=>new TokenPeriod {
            Start=dates[i].ToString("o"),End=(dates[i+1]>now?now:dates[i+1]).ToString("o"),
            Tokens=Difference(values[i],values[i+1],"totalTokens"), Input=Difference(values[i],values[i+1],"totalTokensIn"),Output=Difference(values[i],values[i+1],"totalTokensOut"),Requests=Difference(values[i],values[i+1],"totalCount"),Cost=Difference(values[i],values[i+1],"totalCost"),Error=errors[i]??errors[i+1]
        }).Reverse().ToArray();
    }
}
}
