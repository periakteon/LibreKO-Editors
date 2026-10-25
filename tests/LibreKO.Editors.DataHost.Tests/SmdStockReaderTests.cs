using LibreKO.Editors.Core.Maps;
using LibreKO.Game.World;
public class SmdStockReaderTests
{
    [Fact]
    public void InstalledServerMapsAreOnlyReadAndRetainExactBytes()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"scripts","run.ps1"))) dir=dir.Parent;
        Assert.NotNull(dir);
        var maps=Path.GetFullPath(Path.Combine(dir!.FullName,"..","LibreKO","Server","LibreKO.Game","Map"));
        var paths=Directory.GetFiles(maps,"*.smd").OrderBy(p=>new FileInfo(p).Length).Take(5).Append(Path.Combine(maps,"Moradon2.smd"));
        foreach(var path in paths)
        {
            var input=File.ReadAllBytes(path); var document=SmdCodec.Read(input);
            Assert.Equal(input,SmdCodec.Write(document)); Assert.Equal(input,File.ReadAllBytes(path));
            var stock=SmdFile.Load(path)!; Assert.NotNull(stock);
            Assert.Equal(stock.MapSize,document.Heights.Size); Assert.Equal(stock.HeightMap,document.Heights.Heights);
        }
    }
    [Fact]
    public void EditorOutputIsAcceptedByUnchangedStockReader()
    {
        var doc=SmdCodec.Read(SmdFixtures.Create()); doc.Warps[0]=doc.Warps[0] with{Name="Edited warp"};
        var path=Path.Combine(Path.GetTempPath(),"libreko-smd-stock-"+Guid.NewGuid()+".smd"); File.WriteAllBytes(path,SmdCodec.Write(doc));
        var stock=SmdFile.Load(path)!;
        Assert.Equal("Edited warp",stock.Warps[7].Name); Assert.Equal(uint.MaxValue,stock.Warps[7].Fee);
        Assert.Equal(2,doc.Objects.Count); Assert.Equal(99,stock.ObjectEvents[11].Belong);
        Assert.Equal(8f,stock.RegeneEvents[0].AreaX); Assert.Equal(7f,stock.RegeneEvents[0].AreaZ);
        Assert.Equal(120f,stock.HeightMap[1*5+2]); Assert.Equal(13,stock.GetEventId(1,2));
        Assert.Equal(1,stock.CollisionFaceCount); Assert.Equal(2f,stock.CollisionVertices[0]);
    }
}
