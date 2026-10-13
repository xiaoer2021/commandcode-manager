using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace ModernHost {
public class PortableAccount {
    public string Name { get; set; }
    public string ApiKey { get; set; }
    public string Method { get; set; }
    public string UserId { get; set; }
    public string OrgId { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public bool OfficialUsagePanel { get; set; }
}
public class AccountEnvelope {
    public string Format { get; set; }
    public int Version { get; set; }
    public int Iterations { get; set; }
    public string Salt { get; set; }
    public string IV { get; set; }
    public string Ciphertext { get; set; }
    public string MAC { get; set; }
}
public static class AccountTransfer {
    private const string Format="CommandCodeManager.Accounts";
    private static byte[] Authenticated(AccountEnvelope e) { return Encoding.UTF8.GetBytes(e.Format+"|"+e.Version+"|"+e.Iterations+"|"+e.Salt+"|"+e.IV+"|"+e.Ciphertext); }
    private static byte[] Random(int count) { byte[] value=new byte[count];using(RandomNumberGenerator rng=RandomNumberGenerator.Create())rng.GetBytes(value);return value; }
    private static void Password(string value) { if(string.IsNullOrEmpty(value)||value.Length<8||value.Length>1024)throw new InvalidOperationException("账号包密码需为 8–1024 个字符"); }
    public static void Validate(PortableAccount[] accounts) {
        if(accounts==null||accounts.Length==0||accounts.Length>200)throw new InvalidOperationException("账号包需包含 1–200 个账号");
        foreach(PortableAccount a in accounts) if(a==null||string.IsNullOrWhiteSpace(a.ApiKey)||a.ApiKey.Length>8192||a.ApiKey.Contains("\r")||a.ApiKey.Contains("\n")||(a.Name??"").Length>80||(a.UserId??"").Length>256||(a.OrgId??"").Length>256||(a.UserName??"").Length>256||(a.Email??"").Length>320)throw new InvalidOperationException("账号包中的账号格式不正确");
    }
    public static string Encrypt(PortableAccount[] accounts,string password) {
        Password(password);Validate(accounts);byte[] salt=Random(32),iv=Random(16),keys;
        using(Rfc2898DeriveBytes kdf=new Rfc2898DeriveBytes(password,salt,200000))keys=kdf.GetBytes(64);
        try {
            byte[] plain=Encoding.UTF8.GetBytes(new JavaScriptSerializer {MaxJsonLength=5242880}.Serialize(accounts)),cipher;
            try { using(Aes aes=Aes.Create()) {aes.Key=keys.Take(32).ToArray();aes.IV=iv;using(ICryptoTransform encrypt=aes.CreateEncryptor())cipher=encrypt.TransformFinalBlock(plain,0,plain.Length);} }
            finally {Array.Clear(plain,0,plain.Length);}
            AccountEnvelope envelope=new AccountEnvelope{Format=Format,Version=1,Iterations=200000,Salt=Convert.ToBase64String(salt),IV=Convert.ToBase64String(iv),Ciphertext=Convert.ToBase64String(cipher)};
            using(HMACSHA256 mac=new HMACSHA256(keys.Skip(32).ToArray()))envelope.MAC=Convert.ToBase64String(mac.ComputeHash(Authenticated(envelope)));
            return new JavaScriptSerializer {MaxJsonLength=5242880}.Serialize(envelope);
        } finally {Array.Clear(keys,0,keys.Length);}
    }
    public static PortableAccount[] Decrypt(string content,string password) {
        Password(password);if(content==null||content.Length>5242880)throw new InvalidOperationException("账号包文件过大或不正确");
        try {
            AccountEnvelope e=new JavaScriptSerializer {MaxJsonLength=5242880}.Deserialize<AccountEnvelope>(content);
            if(e==null||e.Format!=Format||e.Version!=1||e.Iterations!=200000)throw new InvalidOperationException("不支持此账号包格式");
            byte[] salt=Convert.FromBase64String(e.Salt),iv=Convert.FromBase64String(e.IV),cipher=Convert.FromBase64String(e.Ciphertext),tag=Convert.FromBase64String(e.MAC);
            if(salt.Length!=32||iv.Length!=16||tag.Length!=32||cipher.Length==0||cipher.Length%16!=0)throw new InvalidOperationException("账号包已损坏");
            byte[] keys;using(Rfc2898DeriveBytes kdf=new Rfc2898DeriveBytes(password,salt,e.Iterations))keys=kdf.GetBytes(64);
            try {
                byte[] expected;using(HMACSHA256 mac=new HMACSHA256(keys.Skip(32).ToArray()))expected=mac.ComputeHash(Authenticated(e));
                int difference=0;for(int i=0;i<tag.Length;i++)difference|=tag[i]^expected[i];if(difference!=0)throw new InvalidOperationException("密码不正确或账号包已被修改");
                byte[] plain;using(Aes aes=Aes.Create()){aes.Key=keys.Take(32).ToArray();aes.IV=iv;using(ICryptoTransform decrypt=aes.CreateDecryptor())plain=decrypt.TransformFinalBlock(cipher,0,cipher.Length);}
                try {PortableAccount[] accounts=new JavaScriptSerializer {MaxJsonLength=5242880}.Deserialize<PortableAccount[]>(Encoding.UTF8.GetString(plain));Validate(accounts);return accounts;}
                finally {Array.Clear(plain,0,plain.Length);}
            } finally {Array.Clear(keys,0,keys.Length);}
        } catch(InvalidOperationException){throw;}catch {throw new InvalidOperationException("账号包已损坏或格式不正确");}
    }
}
}
