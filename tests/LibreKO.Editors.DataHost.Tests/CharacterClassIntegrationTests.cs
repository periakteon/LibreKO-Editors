using LibreKO.Domain;

public class CharacterClassIntegrationTests
{
    [Theory]
    [InlineData(101, true)]
    [InlineData(201, true)]
    [InlineData(301, false)]
    public void LinkedClassRulesRestrictPlayersToPlayableNations(int code, bool expected)
        => Assert.Equal(expected, CharacterClassCatalog.IsPlayerClass(code));
}
