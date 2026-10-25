using System.Diagnostics;
using System.Text.Json;
using MySqlConnector;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.DataHost;
public static class DockerDiscovery {
 public static IReadOnlyList<DockerDatabase> Parse(string json){
  using var doc=JsonDocument.Parse(json);var result=new List<DockerDatabase>();
  foreach(var container in doc.RootElement.EnumerateArray()){
   var config=container.GetProperty("Config");var image=config.GetProperty("Image").GetString()??"";
   var imageName=image.Split('/').Last().Split(':')[0].ToLowerInvariant();
   if(imageName is not ("mariadb" or "mysql" or "mysql-server" or "percona"))continue;
   var env=new Dictionary<string,string>(StringComparer.Ordinal);
   if(config.TryGetProperty("Env",out var vars))foreach(var variable in vars.EnumerateArray()){var entry=variable.GetString()??"";var index=entry.IndexOf('=');if(index>0)env[entry[..index]]=entry[(index+1)..];}
   string Get(params string[] keys)=>keys.Select(k=>env.GetValueOrDefault(k)).FirstOrDefault(v=>!string.IsNullOrEmpty(v))??"";
   var user=Get("MARIADB_USER","MYSQL_USER");if(user=="")user="root";
   var password=user=="root"?Get("MARIADB_ROOT_PASSWORD","MYSQL_ROOT_PASSWORD"):Get("MARIADB_PASSWORD","MYSQL_PASSWORD");
   var database=Get("MARIADB_DATABASE","MYSQL_DATABASE");string server="127.0.0.1";uint port=3306;bool relay=true;
   if(container.TryGetProperty("NetworkSettings",out var network)&&network.TryGetProperty("Ports",out var ports)&&ports.TryGetProperty("3306/tcp",out var bindings)&&bindings.ValueKind==JsonValueKind.Array){
    foreach(var binding in bindings.EnumerateArray()){var ip=binding.GetProperty("HostIp").GetString()??"";if(ip.Contains(':'))continue;if(!uint.TryParse(binding.GetProperty("HostPort").GetString(),out port))continue;server=ip is "" or "0.0.0.0"?"127.0.0.1":ip;relay=false;break;}
   }
   var builder=new MySqlConnectionStringBuilder{Server=server,Port=port,Database=database,UserID=user,Password=password,Pooling=false,ConnectionTimeout=10};
   var name=container.GetProperty("Name").GetString()?.TrimStart('/')??image;
   var hint=relay?"Docker localhost relay":server+":"+port;
   result.Add(new(container.GetProperty("Id").GetString()!,name+" · "+image+" · "+hint,builder.ConnectionString,relay,password==""));
  }
  return result;
 }
 public static async Task<IReadOnlyList<DockerDatabase>> DiscoverAsync(){
  var ids=(await ReadAsync(["ps","--quiet"])).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
  if(ids.Length==0)return [];
  return Parse(await ReadAsync(["inspect",..ids]));
 }
 internal static Process Start(IReadOnlyList<string> args){
  var p=new Process{StartInfo=new("docker"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
  foreach(var arg in args)p.StartInfo.ArgumentList.Add(arg);p.Start();return p;
 }
 static async Task<string> ReadAsync(string[] args){
  try{using var process=Start(args);process.StandardInput.Close();var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}await errors;if(process.ExitCode!=0)throw new InvalidOperationException("Docker is unavailable. Start Docker Desktop and allow this user to access its engine.");return await output;}
  catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("Docker CLI was not found. Install/start Docker Desktop or enter a connection string.");}
 }
}
