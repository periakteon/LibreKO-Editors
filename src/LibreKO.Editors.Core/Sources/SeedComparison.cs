using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Core.Sources;

public static class SeedComparison
{
    static void Verify(WorldTable table,JsonSourceDocument doc,SourceRowToken token)
    { if(ServerJsonCatalog.Format(doc.Snapshot.Key.RelativePath)?.Table!=table) throw new InvalidOperationException("Source and database table differ."); _=doc.Values(token); }

    public static FileChange PrepareSeedUpdate(WorldRowSnapshot row,JsonSourceDocument document,SourceRowToken token,SourceStore store)
    {
        Verify(row.Key.Table,document,token);
        var now=store.Read(document.Snapshot.Key);
        if(now.Exists!=document.Snapshot.Exists || now.Sha256!=document.Snapshot.Sha256) throw new InvalidOperationException("Source changed externally; reopen and compare again.");
        int index=document.Rows.ToList().FindIndex(r=>r.Token==token);
        var clone=JsonSourceDocument.OpenDraft(document.Snapshot,document.Render()); var target=clone.Rows[index].Token;
        var values=clone.Values(target);
        foreach(var field in row.Values)
        {
            if(row.Key.Table==WorldTable.NpcPositions && field.Key=="Index") continue;
            if(SeedRowMatcher.Equal(field.Value,SeedRowMatcher.Value(values,field.Key))) continue;
            var spelling=values.Select(x=>x.Key).SingleOrDefault(x=>x.Equals(field.Key,StringComparison.OrdinalIgnoreCase))??field.Key;
            clone.Set(target,spelling,field.Value);
        }
        return new(Guid.NewGuid(),[new(document.Snapshot,store.Blobs.Put(clone.Render()))]);
    }

    public static WorldChange PrepareDatabaseUpdate(JsonSourceDocument document,SourceRowToken token,WorldRowSnapshot? before,WorldKey target,WorldCatalog? catalog=null)
    {
        WorldKeys.Validate(target); Verify(target.Table,document,token);
        if(before!=null && before.Key!=target) throw new InvalidOperationException("Original database row and explicit target differ.");
        var schema=catalog?.Tables.Single(x=>x.Table==target.Table);
        if(before==null && schema==null) throw new InvalidOperationException("New database rows require offline world metadata and an explicit unused target key.");
        var baseline=before?.Values??schema!.Defaults;
        var fields=schema?.Fields??baseline.Select(x=>new WorldFieldDescriptor(new(x.Key,"Mapped",x.Value?.GetValueKind() switch {JsonValueKind.String=>FieldKind.Text,JsonValueKind.True or JsonValueKind.False=>FieldKind.Boolean,_=>x.Value?.ToString().IndexOfAny(['.','E','e'])>=0?FieldKind.Decimal:FieldKind.Integer},true),x.Key,x.Value==null,null)).ToArray();
        var after=ServerJsonCatalog.Normalize(document.Values(token),fields,baseline);
        foreach(var key in WorldKeys.Fields(target.Table)) after[key]=key switch { "IsMonster"=>JsonValue.Create(target.IsMonster!.Value),"ZoneNum"=>JsonValue.Create(target.Zone!.Value),_=>JsonValue.Create(target.Id) };
        return new(Guid.NewGuid(),[new(target,before,after)],[]);
    }
}
