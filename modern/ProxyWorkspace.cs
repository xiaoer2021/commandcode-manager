using System;
using System.Collections.Generic;
using System.Linq;
using CommandCodeManager;
namespace ModernHost {
public static class ProxyWorkspace {
public static void Normalize(ProxySettings settings){if(settings.AccountIds==null)settings.AccountIds=new List<string>();if(settings.AccessKeys==null)settings.AccessKeys=new List<ProxyAccessKey>();if(!settings.KeysMigrated&&settings.AccessKeys.Count==0&&!string.IsNullOrEmpty(settings.Secret))settings.AccessKeys.Add(new ProxyAccessKey{Id=Guid.NewGuid().ToString("N"),Name="默认 Key",Secret=settings.Secret,Enabled=true,CreatedAt=DateTimeOffset.UtcNow.ToString("o")});settings.KeysMigrated=true;}
public static ProxyAccessKey Create(ProxySettings settings,string name){Normalize(settings);if(settings.AccessKeys.Count>=30)throw new InvalidOperationException("最多保留 30 个代理 Key");var key=new ProxyAccessKey{Id=Guid.NewGuid().ToString("N"),Name=string.IsNullOrWhiteSpace(name)?"Key "+(settings.AccessKeys.Count+1):name.Trim().Substring(0,Math.Min(40,name.Trim().Length)),Secret=Core.Encrypt(LocalProxy.GenerateKey()),Enabled=true,CreatedAt=DateTimeOffset.UtcNow.ToString("o")};settings.AccessKeys.Add(key);if(string.IsNullOrEmpty(settings.Secret))settings.Secret=key.Secret;return key;}
public static string[] EnabledKeys(ProxySettings settings){Normalize(settings);return settings.AccessKeys.Where(k=>k.Enabled).Select(k=>Core.Decrypt(k.Secret)).ToArray();}
public static string Primary(ProxySettings settings){Normalize(settings);var key=settings.AccessKeys.FirstOrDefault(k=>k.Enabled);return key==null?null:Core.Decrypt(key.Secret);}
public static ProxyAccessKey Find(ProxySettings settings,string id){Normalize(settings);var key=settings.AccessKeys.FirstOrDefault(k=>k.Id==id);if(key==null)throw new InvalidOperationException("代理 Key 不存在");return key;}
public static string[] Reconcile(ProxySettings settings,IEnumerable<string> available){var ready=new HashSet<string>(available);return (settings.AutoJoin?ready:settings.AccountIds.Where(ready.Contains)).Distinct().ToArray();}
}
}
