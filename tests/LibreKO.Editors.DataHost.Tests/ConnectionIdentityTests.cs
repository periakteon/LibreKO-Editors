using LibreKO.Editors.DataHost;
public class ConnectionIdentityTests {
 [Fact] public void DockerHistorySurvivesPasswordEntryAndRelayPortChanges(){
  var first=ConnectionIdentity.For("Server=127.0.0.1;Port=40001;Database=game;User=editor;Password=entered;","Server=127.0.0.1;Port=40001;Database=game;User=editor;","container-123");
  var second=ConnectionIdentity.For("server=127.0.0.1;port=50001;database=game;uid=other;pwd=new;","server=127.0.0.1;port=50001;database=game;uid=root;pwd=old;","container-123");
  Assert.Equal("docker:container-123/game",first);Assert.Equal(first,second);
  Assert.NotEqual(first,ConnectionIdentity.For("server=127.0.0.1;port=50001;database=other;","server=127.0.0.1;port=50001;database=game;","container-123"));
 }
}
