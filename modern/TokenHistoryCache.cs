using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CommandCodeManager;
namespace ModernHost {
public class HistoryQuery {
    public string Grain { get; set; }
    public int Page { get; set; }
    public bool Force { get; set; }
    public string Start { get; set; }
    public string End { get; set; }
}
public class HistoryWindow {
    public string AccountId { get; set; }
    public string Grain { get; set; }
    public int Page { get; set; }
    public TokenPeriod[] Rows { get; set; }
    public string UpdatedAt { get; set; }
    public bool HasCache { get; set; }
    public bool Complete { get; set; }
    public string Error { get; set; }
    public string Start { get; set; }
    public string End { get; set; }
    public int Pages { get; set; }
    public bool HasNext { get; set; }
}
public class CachedTokenPeriod {
    public string AccountId { get; set; }
    public string Grain { get; set; }
    public TokenPeriod Row { get; set; }
    public string UpdatedAt { get; set; }
    public bool Closed { get; set; }
    public string BoundaryEnd { get; set; }
}
public class TokenHistoryCache {
    private readonly string file;
    private readonly object gate=new object();
    private readonly Func<string,string,DateTimeOffset,DateTimeOffset,Task<Dictionary<string,object>>> fetch;
    private readonly Dictionary<string,CachedTokenPeriod> records=new Dictionary<string,CachedTokenPeriod>();
    private static string RecordKey(string accountId,string grain,DateTimeOffset start,DateTimeOffset end) { return accountId+"|"+grain+"|"+start.ToUniversalTime().ToString("o")+"|"+end.ToUniversalTime().ToString("o"); }
    public static DateTimeOffset[] Dates(HistoryQuery query,DateTimeOffset now,out int pages) { if(!string.IsNullOrEmpty(query.Start)||!string.IsNullOrEmpty(query.End))return TokenHistory.RangeBoundaries(query.Grain,query.Start,query.End,query.Page,now,out pages);pages=61;return TokenHistory.Boundaries(query.Grain,query.Page,now); }
    private static bool Valid(TokenPeriod row) { return row!=null && row.Tokens.HasValue && row.Tokens>=0 && string.IsNullOrEmpty(row.Error); }
    public TokenHistoryCache(string path,Func<string,string,DateTimeOffset,DateTimeOffset,Task<Dictionary<string,object>>> fetcher=null) {
        file=path; fetch=fetcher??TokenHistory.Snapshot;
        try {
            if(!File.Exists(file)||new FileInfo(file).Length>8388608) return;
            List<CachedTokenPeriod> saved=new JavaScriptSerializer { MaxJsonLength=8388608 }.Deserialize<List<CachedTokenPeriod>>(File.ReadAllText(file));
            if(saved==null) return;
            foreach(CachedTokenPeriod item in saved.Take(4096)) {
                DateTimeOffset start;
                if(item!=null && !string.IsNullOrEmpty(item.AccountId) && (item.Grain=="day"||item.Grain=="week"||item.Grain=="month") && Valid(item.Row) && Core.TryTime(item.Row.Start,out start)) { DateTimeOffset finish;if(!Core.TryTime(item.BoundaryEnd,out finish))finish=item.Grain=="day"?start.AddDays(1):item.Grain=="week"?start.AddDays(7):start.AddMonths(1);item.BoundaryEnd=finish.ToString("o");records[RecordKey(item.AccountId,item.Grain,start,finish)]=item; }
            }
        } catch { }
    }
    public HistoryWindow Read(string accountId,string grain,int page,DateTimeOffset now) {return Read(accountId,new HistoryQuery{Grain=grain,Page=page},now);}
    public HistoryWindow Read(string accountId,HistoryQuery query,DateTimeOffset now) {
        int pages;DateTimeOffset[] dates=Dates(query,now,out pages);
        lock(gate) {
            List<TokenPeriod> rows=new List<TokenPeriod>(); string updated=null;
            for(int i=dates.Length-2;i>=0;i--) {
                CachedTokenPeriod item;
                if(records.TryGetValue(RecordKey(accountId,query.Grain,dates[i],dates[i+1]),out item)) {
                    rows.Add(item.Row); if(updated==null||string.CompareOrdinal(item.UpdatedAt,updated)>0) updated=item.UpdatedAt;
                } else rows.Add(new TokenPeriod { Start=dates[i].ToString("o"),End=(dates[i+1]>now?now:dates[i+1]).ToString("o") });
            }
            return new HistoryWindow { AccountId=accountId,Grain=query.Grain,Page=query.Page,Start=query.Start,End=query.End,Pages=pages,HasNext=query.Page+1<pages,Rows=rows.ToArray(),UpdatedAt=updated,HasCache=rows.Any(Valid),Complete=rows.All(Valid) };
        }
    }
    public async Task<HistoryWindow[]> Refresh(string accountId,string key,string org,HistoryQuery[] queries,DateTimeOffset now,Action<int,int> progress=null) {
        var windows=queries.Select(q=>{int pages;return new {Query=q,Dates=Dates(q,now,out pages)};}).ToArray();
        HashSet<DateTimeOffset> needed=new HashSet<DateTimeOffset>();
        lock(gate) foreach(var window in windows) for(int i=0;i<window.Dates.Length-1;i++) {
            CachedTokenPeriod cached;
            if(!window.Query.Force&&records.TryGetValue(RecordKey(accountId,window.Query.Grain,window.Dates[i],window.Dates[i+1]),out cached)&&cached.Closed&&Valid(cached.Row)) continue;
            needed.Add(window.Dates[i]); needed.Add(window.Dates[i+1]);
        }
        Dictionary<DateTimeOffset,Dictionary<string,object>> snapshots=new Dictionary<DateTimeOffset,Dictionary<string,object>>();
        Dictionary<DateTimeOffset,string> errors=new Dictionary<DateTimeOffset,string>();
        int completed=0;await Task.WhenAll(needed.Select(async date=>{
            Dictionary<string,object> value=null; string error=null;
            try { value=await fetch(key,org,date,now); } catch(Exception e) { error=e.Message; }
            lock(snapshots) { snapshots[date]=value; if(error!=null) errors[date]=error; }
            int done=System.Threading.Interlocked.Increment(ref completed);if(progress!=null)progress(done,needed.Count);
        }));
        List<HistoryWindow> results=new List<HistoryWindow>(); bool changed=false;
        lock(gate) {
            foreach(var window in windows) {
                List<string> failed=new List<string>();
                for(int i=0;i<window.Dates.Length-1;i++) {
                    DateTimeOffset start=window.Dates[i],end=window.Dates[i+1]; string id=RecordKey(accountId,window.Query.Grain,start,end); CachedTokenPeriod old;
                    if(!window.Query.Force&&records.TryGetValue(id,out old)&&old.Closed&&Valid(old.Row)) continue;
                    Dictionary<string,object> older,newer; string error;
                    if(!snapshots.TryGetValue(start,out older)||older==null||!snapshots.TryGetValue(end,out newer)||newer==null) {
                        failed.Add(errors.TryGetValue(start,out error)?error:errors.TryGetValue(end,out error)?error:"用量查询暂未完成"); continue;
                    }
                    TokenPeriod row=new TokenPeriod { Start=start.ToString("o"),End=(end>now?now:end).ToString("o"),Tokens=TokenHistory.Difference(older,newer,"totalTokens"),Input=TokenHistory.Difference(older,newer,"totalTokensIn"),Output=TokenHistory.Difference(older,newer,"totalTokensOut"),Requests=TokenHistory.Difference(older,newer,"totalCount"),Cost=TokenHistory.Difference(older,newer,"totalCost") };
                    if(!Valid(row)) { failed.Add("官方用量数据暂未完整，已保留本地记录"); continue; }
                    records[id]=new CachedTokenPeriod { AccountId=accountId,Grain=window.Query.Grain,Row=row,BoundaryEnd=end.ToString("o"),UpdatedAt=now.ToUniversalTime().ToString("o"),Closed=end<=now }; changed=true;
                }
                HistoryWindow result=Read(accountId,window.Query,now); result.Error=string.Join("；",failed.Distinct()); results.Add(result);
            }
            if(changed) {
                foreach(string id in records.Where(p=>p.Value.AccountId==accountId).OrderByDescending(p=>p.Value.UpdatedAt).Skip(512).Select(p=>p.Key).ToArray()) records.Remove(id);
                foreach(string id in records.OrderByDescending(p=>p.Value.UpdatedAt).Skip(4096).Select(p=>p.Key).ToArray()) records.Remove(id);
                try { Save(); } catch(Exception e) { foreach(HistoryWindow result in results) result.Error="数据已更新，但本地保存失败："+e.Message; }
            }
        }
        return results.ToArray();
    }
    public void Remove(string accountId) { lock(gate) { foreach(string id in records.Where(p=>p.Value.AccountId==accountId).Select(p=>p.Key).ToArray()) records.Remove(id); Save(); } }
    private void Save() { Core.AtomicWrite(file,new JavaScriptSerializer { MaxJsonLength=8388608 }.Serialize(records.Values.ToArray())); }
}
}
