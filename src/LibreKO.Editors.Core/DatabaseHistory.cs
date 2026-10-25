using System.Security.Cryptography;
using System.Text;
namespace LibreKO.Editors.Core;
public static class DatabaseHistory {
 public static string DirectoryFor(string workspaceRoot,string identity)=>Path.Combine(Path.GetFullPath(workspaceRoot),"database-history",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant());
}
