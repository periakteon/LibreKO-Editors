using System.Text.Json;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core;
public static class WorkspacePaths {
 public static string ResolveOwnedPath(string root,string relative) {
  relative=relative.Replace('/',Path.DirectorySeparatorChar);
  if(Path.IsPathRooted(relative)||relative.Split(Path.DirectorySeparatorChar).Any(x=>x=="..")||relative.Contains(':'))throw new InvalidOperationException("The path must stay inside the editor workspace.");
  root=Path.GetFullPath(root);var path=Path.GetFullPath(Path.Combine(root,relative));
  if(!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Path escapes workspace.");
  for(var p=Path.GetDirectoryName(path);p!=null&&p.Length>=root.Length;p=Path.GetDirectoryName(p))if(System.IO.Directory.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked workspace paths are not supported.");
  if((File.Exists(path)||System.IO.Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked workspace files are not supported.");
  return path;
 }
 public static void AtomicWrite(string path,string text){System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(tmp,text);File.Move(tmp,path,true);}
}
public static class ProfileStore {
 public static WorkspaceProfile? Load(string path)=>File.Exists(path)?JsonSerializer.Deserialize<WorkspaceProfile>(File.ReadAllText(path),JsonConfig.Options):null;
 public static void Save(string path,WorkspaceProfile profile)=>WorkspacePaths.AtomicWrite(path,JsonSerializer.Serialize(profile,JsonConfig.Options));
}
