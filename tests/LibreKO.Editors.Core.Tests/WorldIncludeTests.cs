using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
public class WorldIncludeTests
{
    [Fact] public void IncludingAnExistingRowPreservesOriginalsAndTheUndoTimeline()
    {
        WorldRowSnapshot Row(int id) { var v=new JsonObject { ["Id"]=id,["Name"]="Original" }; return new(new(WorldTable.MapObjects,id),v,RecordFingerprint.Compute(v)); }
        var a=Row(1); var b=Row(2); var d=new WorldDraft([a]); d.Set(a.Key,"Name",JsonValue.Create("Edited")); d.Include([b]);
        Assert.Single(d.Build(Guid.NewGuid()).Rows); d.Set(b.Key,"Name",JsonValue.Create("Second")); var batch=d.Build(Guid.NewGuid()); Assert.All(batch.Rows,r=>Assert.NotNull(r.Before));
        d.Undo(false); Assert.Single(d.Build(Guid.NewGuid()).Rows); d.Undo(false); Assert.False(d.IsDirty); Assert.Equal(2,d.Rows.Count);
        d.Undo(true); Assert.Single(d.Build(Guid.NewGuid()).Rows); Assert.Equal("Original",d.Rows.Single(r=>r.Key==b.Key).Values["Name"]!.ToString());
    }
}
