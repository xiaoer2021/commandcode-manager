using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModernHost;
public class HistoryCacheChecks {
    public int Calls; public bool Offline;
    public Task<Dictionary<string,object>> Fetch(string key,string org,DateTimeOffset start,DateTimeOffset now) {
        if(start>=now) return Task.FromResult(Values(0));
        Interlocked.Increment(ref Calls); if(Offline) throw new InvalidOperationException("offline fixture");
        return Task.FromResult(Values(Math.Max(0,(now-start).TotalSeconds)));
    }
    private Dictionary<string,object> Values(double n) { return new Dictionary<string,object>{{"totalTokens",n*10},{"totalTokensIn",n*8},{"totalTokensOut",n*2},{"totalCount",n},{"totalCost",n*.01}}; }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    public static string Run(string root) {
        HistoryCacheChecks fixture=new HistoryCacheChecks(); string file=Path.Combine(root,"cache.json"); TokenHistoryCache cache=new TokenHistoryCache(file,fixture.Fetch);
        DateTimeOffset now=DateTimeOffset.Parse("2026-10-13T10:00:00Z"); HistoryQuery[] recent=new string[]{"day","week","month"}.Select(g=>new HistoryQuery{Grain=g,Page=0}).ToArray();
        Check(!cache.Read("account-a","day",0,now).HasCache,"cold cache unexpectedly populated");
        HistoryWindow[] initial=cache.Refresh("account-a","secret-not-for-disk","org-a",recent,now).GetAwaiter().GetResult();
        Check(initial.All(r=>r.Complete),"initial warm incomplete"); Check(fixture.Calls<19,"shared boundaries were fetched repeatedly");
        int first=fixture.Calls; HistoryWindow day=cache.Read("account-a","day",0,now); Check(fixture.Calls==first,"cache read issued a query");
        Check(File.Exists(file)&&!File.ReadAllText(file).Contains("secret-not-for-disk"),"secret leaked to cache");
        TokenHistoryCache restarted=new TokenHistoryCache(file,fixture.Fetch); Check(restarted.Read("account-a","day",0,now).Complete&&fixture.Calls==first,"restart lost cached data");
        Check(!restarted.Read("account-b","day",0,now).HasCache,"account cache crossed scope");
        cache.Refresh("account-a","key","org-a",recent,now.AddMinutes(5)).GetAwaiter().GetResult(); Check(fixture.Calls-first<=3,"automatic refresh re-fetched closed periods");
        Check(cache.Read("account-a","day",0,now).Rows[1].Tokens==day.Rows[1].Tokens,"closed period changed");
        HistoryWindow before=cache.Read("account-a","day",0,now); fixture.Offline=true;
        HistoryWindow failed=cache.Refresh("account-a","key","org-a",new HistoryQuery[]{recent[0]},now.AddMinutes(10)).GetAwaiter().GetResult()[0];
        Check(failed.HasCache&&failed.Complete&&!string.IsNullOrEmpty(failed.Error)&&failed.Rows[0].Tokens==before.Rows[0].Tokens,"failed update destroyed cached values"); fixture.Offline=false;
        DateTimeOffset tomorrow=now.AddDays(1); HistoryWindow rotated=cache.Refresh("account-a","key","org-a",new HistoryQuery[]{recent[0]},tomorrow).GetAwaiter().GetResult()[0];
        Check(rotated.Complete&&rotated.Rows[0].Start.StartsWith("2026-10-14")&&rotated.Rows[1].Tokens>before.Rows[0].Tokens,"midnight rollover lost or failed to close yesterday");
        HistoryQuery oldPage=new HistoryQuery{Grain="day",Page=1}; cache.Refresh("account-a","key","org-a",new HistoryQuery[]{oldPage},tomorrow).GetAwaiter().GetResult(); int oldCalls=fixture.Calls;
        cache.Refresh("account-a","key","org-a",new HistoryQuery[]{oldPage},tomorrow.AddMinutes(1)).GetAwaiter().GetResult(); Check(fixture.Calls==oldCalls,"closed historical page was queried again");
        oldPage.Force=true; cache.Refresh("account-a","key","org-a",new HistoryQuery[]{oldPage},tomorrow.AddMinutes(2)).GetAwaiter().GetResult(); Check(fixture.Calls>oldCalls,"manual refresh did not re-check official historical values");
        cache.Remove("account-a"); Check(!new TokenHistoryCache(file,fixture.Fetch).Read("account-a","day",0,tomorrow).HasCache,"removed account cache survived");
        return "PASS: persistent warm cache, instant reads, shared boundary dedup, account isolation, incremental refresh, failed-query preservation, midnight rollover, historical reuse, account removal";
    }
}
