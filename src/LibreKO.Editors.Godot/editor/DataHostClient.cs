using System.Diagnostics;
using System.Text.Json;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.Desktop;
public sealed class DataHostClient:IEditorDataClient,IDisposable {
 readonly Process process;readonly SemaphoreSlim gate=new(1);bool closed;
 public CancellationToken OperationCancellation {get;set;}
 public bool IsClosed=>closed; public ActivityTracker? Loading {get;set;}
 public DataHostClient(string host){var executable=System.IO.Path.ChangeExtension(host,".exe");var native=System.IO.File.Exists(executable);process=new(){StartInfo=new(native?executable:"dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};if(!native)process.StartInfo.ArgumentList.Add(host);process.Start();process.ErrorDataReceived+=(_,_)=>{};process.BeginErrorReadLine();}
 public async Task<T> SendAsync<T>(string method,object payload,CancellationToken cancellationToken=default){using var activity=Loading?.Begin(method.StartsWith("references.")?"references":"data",method.StartsWith("references.")?"Resolving names…":"Loading data…");var id=Guid.NewGuid();var frame=JsonSerializer.Serialize(new RpcRequest(1,id,method,JsonSerializer.SerializeToElement(payload,JsonConfig.Options)),JsonConfig.Options);Protocol.ValidateOutbound(frame);using var operation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,OperationCancellation);cancellationToken=operation.Token;await gate.WaitAsync(cancellationToken);try{await process.StandardInput.WriteLineAsync(frame);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(30));var text=await process.StandardOutput.ReadLineAsync(timeout.Token)??throw new IOException("Database helper closed.");var r=JsonSerializer.Deserialize<RpcResponse>(text,JsonConfig.Options)!;if(r.Id!=id)throw new IOException("Database helper response mismatch.");if(r.Error!=null)throw new InvalidOperationException(r.Error.Message);return r.Result!.Value.Deserialize<T>(JsonConfig.Options)!;}catch(OperationCanceledException){Dispose();throw;}catch(IOException){Dispose();throw;}finally{gate.Release();}}
 public void Dispose(){if(closed)return;closed=true;try{process.StandardInput.Close();if(!process.WaitForExit(500))process.Kill(true);}catch{}process.Dispose();}
}
