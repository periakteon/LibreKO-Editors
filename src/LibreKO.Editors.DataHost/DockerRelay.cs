using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MySqlConnector;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.DataHost;
// A transient Docker exec stream carries MySQL bytes; no container or port mapping is changed.
public sealed class DockerRelay:IDisposable {
 readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource stop=new();
 readonly System.Collections.Concurrent.ConcurrentDictionary<int,Process> children=new();readonly string containerId;
 public uint Port=>(uint)((IPEndPoint)listener.LocalEndpoint).Port;
 public DockerRelay(string id){containerId=id;listener.Start();_=Accept();}
 public string ConnectionString(DockerDatabase db){var b=new MySqlConnectionStringBuilder(db.ConnectionString){Server="127.0.0.1",Port=Port,Pooling=false};return b.ConnectionString;}
 async Task Accept(){try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);_=Forward(client);}}catch(OperationCanceledException){}catch(ObjectDisposedException){}}
 async Task Forward(TcpClient client){
  Process? process=null;
  try{
   // Fixed script: container ID is an ArgumentList entry, never interpolated into shell code.
   process=DockerDiscovery.Start(["exec","-i",containerId,"/bin/bash","-c","exec 3<>/dev/tcp/127.0.0.1/3306 || exit 1; cat <&3 & reader=$!; cat >&3; kill $reader 2>/dev/null; wait $reader 2>/dev/null; exit 0"]);
   children.TryAdd(process.Id,process);var errors=process.StandardError.ReadToEndAsync();
   var network=client.GetStream();var incoming=network.CopyToAsync(process.StandardInput.BaseStream,stop.Token);var outgoing=process.StandardOutput.BaseStream.CopyToAsync(network,stop.Token);
   await Task.WhenAny(incoming,outgoing);process.StandardInput.Close();
   if(!process.HasExited)process.Kill(true);await errors;
  }catch(Exception e) when(e is IOException or OperationCanceledException or SocketException or InvalidOperationException or System.ComponentModel.Win32Exception){}
  finally{client.Dispose();if(process!=null){children.TryRemove(process.Id,out _);try{if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();}}
 }
 public void Dispose(){stop.Cancel();listener.Stop();foreach(var p in children.Values)try{if(!p.HasExited)p.Kill(true);}catch{}}
}
