using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModernHost;
public static class OpenCodeProxyChecks {
private static void Check(bool pass,string message){if(!pass)throw new Exception(message);}
private sealed class Handler:HttpMessageHandler {
public int Posts;public string Url,Body,Session;public bool Safe=true;
protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){Safe&=r.RequestUri.Host=="opencode.ai"&&r.Headers.Authorization.Parameter=="fixture-go";if(r.Method==HttpMethod.Get)return Reply(200,"{\"data\":[{\"id\":\"kimi-k2.6\"},{\"id\":\"gpt-6-luna\"},{\"id\":\"unknown-new-model\"}]}");Posts++;Url=r.RequestUri.ToString();Body=await r.Content.ReadAsStringAsync();Session=r.Headers.Contains("x-opencode-session")?r.Headers.GetValues("x-opencode-session").First():null;return Reply(200,"{\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":2}}");}
private static HttpResponseMessage Reply(int code,string body){return new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
}
public static void Run(){RunAsync().GetAwaiter().GetResult();}
private static async Task RunAsync(){var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();var handler=new Handler();string local=LocalProxy.GenerateKey();var account=new ProxyAccount{Id="go",Name="Go fixture",Provider="opencode-go",Key="fixture-go"};using(var proxy=new LocalProxy(()=>new[]{account},handler))using(var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri("http://127.0.0.1:"+port)}){client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",local);proxy.Configure(port,local,new[]{"go"},true);string models=await client.GetStringAsync("/v1/models");Check(models.Contains("opencode-go/kimi-k2.6")&&!models.Contains("unknown-new-model"),"Go catalog namespace or route filter failed");client.DefaultRequestHeaders.TryAddWithoutValidation("x-opencode-session","fixture-session");var response=await client.PostAsync("/v1/chat/completions",new StringContent("{\"model\":\"opencode-go/kimi-k2.6\"}",Encoding.UTF8,"application/json"));Check(response.IsSuccessStatusCode&&handler.Url=="https://opencode.ai/inference/go/v1/chat/completions"&&handler.Body.Contains("\"model\":\"kimi-k2.6\"")&&handler.Session=="fixture-session"&&handler.Safe,"Go path, model, session or credential isolation failed");response=await client.PostAsync("/v1/chat/completions",new StringContent("{\"model\":\"opencode-go/gpt-6-luna\"}",Encoding.UTF8,"application/json"));Check(response.IsSuccessStatusCode&&handler.Posts==2&&handler.Url=="https://opencode.ai/inference/go/v1/chat/completions"&&handler.Body.Contains("\"model\":\"gpt-6-luna\""),"alternate API path was not transparently forwarded");account.Provider="opencode-zen";proxy.ClearCooldown();models=await client.GetStringAsync("/v1/models");Check(models.Contains("opencode-zen/gpt-6-luna"),"Zen catalog failed");response=await client.PostAsync("/v1/responses",new StringContent("{\"model\":\"opencode-zen/gpt-6-luna\"}",Encoding.UTF8,"application/json"));Check(response.IsSuccessStatusCode&&handler.Url=="https://opencode.ai/inference/v1/responses","Zen upstream route failed");response=await client.PostAsync("/v1/chat/completions",new StringContent("{\"model\":\"kimi-k2.6\"}",Encoding.UTF8,"application/json"));Check((int)response.StatusCode==400&&handler.Posts==3,"unqualified model silently crossed platform");}Console.WriteLine("PASS OpenCode Go/Zen paths, model isolation, protocol compatibility, session headers and credentials");}
}
