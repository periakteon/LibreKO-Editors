using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core;
public sealed class CompatibilityResourceAudit(Func<string,bool> exists,CompatibilityReportBuilder report){
 readonly Dictionary<string,bool> seen=new(StringComparer.Ordinal);public int Checked{get;private set;}
 public string? Resolve(IEnumerable<string> alternatives,string kind,RecordKey? record=null){var paths=alternatives.Distinct().ToArray();if(paths.Length==0)return null;var chosen=paths.FirstOrDefault(exists)??paths[0];return Require(chosen,kind,record)?chosen:null;}
 public bool Require(string path,string kind,RecordKey? record=null){
  if(seen.TryGetValue(path,out var previous))return previous;seen[path]=false;
  if(!path.StartsWith("res://assets/",StringComparison.Ordinal)||path.Contains('\\')||path[6..].Split('/').Any(x=>x is ".." or ".")){
   report.Add(new("asset.path",CompatibilitySeverity.Warning,"Assets",path,"Unsupported or unsafe virtual resource path.",record));return false;
  }
  Checked++;if(exists(path)){seen[path]=true;return true;}report.Add(new("asset.missing",CompatibilitySeverity.Warning,"Assets",path,$"Missing {kind}. The database record is preserved.",record));return false;
 }
}
