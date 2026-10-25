using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
public class MapFamilyTests
{
    [Fact] public void ExplicitDataWinsAndSharedFamiliesRequireTheSameSmd()
    {
        MapFamilyEntry[] maps = [new(21,"Eslant I"," a.smd "),new(71,"Eslant II","A.SMD"),new(72,"Eslant III","b.smd"),new(73,"Different IV","a.smd"),new(74,"Eslant V","")];
        Assert.Equal(71,MapFamily.Resolve(71,maps,[21,71])); Assert.Equal(21,MapFamily.Resolve(71,maps,[21]));
        Assert.Equal(72,MapFamily.Resolve(72,maps,[21])); Assert.Equal(74,MapFamily.Resolve(74,maps,[21]));
        Assert.Equal(new[]{21,71},MapFamily.TerrainCandidates(72,maps,[21,71,73]));
    }
}
