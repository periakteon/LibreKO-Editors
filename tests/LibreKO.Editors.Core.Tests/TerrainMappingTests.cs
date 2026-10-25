using LibreKO.Editors.Core.Maps;
public class TerrainMappingTests
{
    [Fact] public void CustomSmdCannotAdoptBundledTerrainFromItsZoneNumber()
    {
        var choice=TerrainMapping.Resolve(21,"custom_map.smd",null,new Dictionary<int,string>{{21,"moradon"}},s=>s=="moradon");Assert.False(choice.Available);Assert.Equal("custom_map",choice.Stem);
    }
    [Fact] public void MoradonServerFilenameCanUseTheClientsAuditedTerrainMapping()
    {
        var client=new Dictionary<int,string>{{21,"moradon"}};
        var choice=TerrainMapping.Resolve(21,"Moradon2.smd",null,client,stem=>stem=="moradon");
        Assert.Equal("moradon",choice.Stem);Assert.Contains("Client zone mapping",choice.Source);
        var direct=TerrainMapping.Resolve(21,"custom_map.smd",null,client,stem=>stem is "custom_map" or "moradon");Assert.Equal("custom_map",direct.Stem);
        var custom=TerrainMapping.Resolve(21,"Moradon2.smd",new Dictionary<int,string>{{21,"replacement"}},client,stem=>stem=="moradon");
        Assert.Equal("replacement",custom.Stem);Assert.Contains("Explicit",custom.Source);Assert.False(custom.Available);
    }
}
