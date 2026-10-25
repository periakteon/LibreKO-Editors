using MySqlConnector;
namespace LibreKO.Editors.DataHost;

public static class ConnectionIdentity {
 public static string For(string connection,string? preparedDockerConnection,string? containerId){
  var current=new MySqlConnectionStringBuilder(connection);
  if(preparedDockerConnection!=null&&containerId!=null){
   var prepared=new MySqlConnectionStringBuilder(preparedDockerConnection);
   if(string.Equals(current.Server,prepared.Server,StringComparison.OrdinalIgnoreCase)&&current.Port==prepared.Port)
    return "docker:"+containerId+"/"+current.Database;
  }
  return current.Server.ToLowerInvariant()+":"+current.Port+"/"+current.Database;
 }
}
