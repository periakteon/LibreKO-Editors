using LibreKO.Editors.DataHost;
using MySqlConnector;
public class DockerDiscoveryTests {
 const string Inspect="""[{"Id":"abc123","Name":"/libreko-db-1","Config":{"Image":"mariadb:11","Env":["MARIADB_DATABASE=ko","MARIADB_USER=editor","MARIADB_PASSWORD=secret;with=punctuation","MARIADB_ROOT_PASSWORD=root"]},"NetworkSettings":{"Ports":{"3306/tcp":[{"HostIp":"0.0.0.0","HostPort":"33306"},{"HostIp":"::","HostPort":"33306"}]}}}]""";
 [Fact]public void PublishedPortProducesEscapedConnection(){var candidate=DockerDiscovery.Parse(Inspect).Single();var builder=new MySqlConnectionStringBuilder(candidate.ConnectionString);Assert.Equal("127.0.0.1",builder.Server);Assert.Equal(33306u,builder.Port);Assert.Equal("ko",builder.Database);Assert.Equal("secret;with=punctuation",builder.Password);Assert.False(candidate.NeedsRelay);Assert.DoesNotContain("secret",candidate.Label);}
 [Fact]public void UnpublishedPortRequiresLocalRelay(){var candidate=DockerDiscovery.Parse(Inspect.Replace("""[{"HostIp":"0.0.0.0","HostPort":"33306"},{"HostIp":"::","HostPort":"33306"}]""","null")).Single();Assert.True(candidate.NeedsRelay);}
 [Fact]public void FileSecretsAreReportedWithoutReadingContainerFiles(){var candidate=DockerDiscovery.Parse(Inspect.Replace("MARIADB_PASSWORD=secret;with=punctuation","MARIADB_PASSWORD_FILE=/run/secrets/password")).Single();Assert.True(candidate.NeedsPassword);Assert.Equal("",new MySqlConnectionStringBuilder(candidate.ConnectionString).Password);}
 [Fact]public void UnrelatedImagesAreIgnored(){Assert.Empty(DockerDiscovery.Parse(Inspect.Replace("mariadb:11","libreko-game")));}
}
