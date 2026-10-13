using System;
using System.Globalization;
using System.Linq;
using System.Text;
using CommandCodeManager;
namespace ModernHost {
public static class UsageCsv {
    public static string Cell(string value) {value=value??"";string trimmed=value.TrimStart();if(trimmed.StartsWith("=")||trimmed.StartsWith("+")||trimmed.StartsWith("-")||trimmed.StartsWith("@"))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
    private static string Number(double? n){return n.HasValue?n.Value.ToString("0.########",CultureInfo.InvariantCulture):"";}
    private static string Time(string value){DateTimeOffset date;return Core.TryTime(value,out date)?date.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture):value;}
    public static string Create(Account account,TokenPeriod[] rows) {
        if(rows==null||rows.Any(r=>!r.Tokens.HasValue||!string.IsNullOrEmpty(r.Error)))throw new InvalidOperationException("部分数据尚未获取完整，请刷新后再导出");
        StringBuilder text=new StringBuilder("\uFEFF账号备注,账号名称,开始时间（北京时间）,结束时间（北京时间）,总Token,输入Token,输出Token,请求数,费用（USD）\r\n");
        foreach(TokenPeriod row in rows.OrderBy(r=>r.Start))text.AppendLine(string.Join(",",new[]{Cell(account.Name),Cell(account.Data==null?"":account.Data.UserName??account.Data.Email),Cell(Time(row.Start)),Cell(Time(row.End)),Number(row.Tokens),Number(row.Input),Number(row.Output),Number(row.Requests),Number(row.Cost)}));return text.ToString();
    }
}
}
