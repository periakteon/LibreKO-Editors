using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Core.World;
public static class ItemBoardEdits
{
    public static bool IsPoolReference(string kind,int id)=>kind=="drop"&&id is >=100 and <100_000_000;
    static void DropSlot(int slot){if(slot is <1 or >7)throw new InvalidOperationException("Choose drop slot 1–7.");}
    public static void SwapDropSlots(WorldDraft draft,WorldKey key,int from,int to)
    {
        DropSlot(from);DropSlot(to);if(key.Table!=WorldTable.NpcItems)throw new InvalidOperationException("Choose a typed drop group.");
        var row=draft.Rows.Single(r=>r.Key==key);var values=(JsonObject)row.Values.DeepClone();
        foreach(var prefix in new[]{"Item","Percent"}){values[prefix+from]=row.Values[prefix+to]?.DeepClone();values[prefix+to]=row.Values[prefix+from]?.DeepClone();}
        draft.Apply([new(key,row,values)]);
    }
    static SourceJsonRow? ShopSlot(JsonSourceDocument json,int group,int line,int index)
    {
        if(group<=0||line is <0 or >255||index is <0 or >255)throw new InvalidOperationException("Shop group must be positive; line and slot must be 0–255.");
        var rows=json.Rows.Where(r=>FieldValueParser.ReadInt32(r.Values["SellingGroup"])==group&&FieldValueParser.ReadInt32(r.Values["Line"])==line&&FieldValueParser.ReadInt32(r.Values["Index"])==index).ToArray();
        if(rows.Length>1)throw new InvalidOperationException("This shop position has duplicate source rows. Resolve the exact rows in Source files first.");
        return rows.SingleOrDefault();
    }
    public static void SetShopSlot(JsonSourceDocument json,int group,int line,int index,int itemId)
    {
        if(itemId<0)throw new InvalidOperationException("Item ID cannot be negative.");
        var row=ShopSlot(json,group,line,index);
        if(itemId==0){if(row!=null)json.Remove(row.Token);}
        else if(row!=null)json.Set(row.Token,"ItemId",JsonValue.Create(itemId));
        else json.Insert(new JsonObject{["SellingGroup"]=group,["Line"]=line,["Index"]=index,["ItemId"]=itemId},json.RowCount);
    }
    public static void SwapShopSlots(JsonSourceDocument json,int group,int fromLine,int from,int toLine,int to)
    {
        // Validate both positions before changing either.
        var a=ShopSlot(json,group,fromLine,from);var b=ShopSlot(json,group,toLine,to);
        if(a!=null){json.Set(a.Token,"Line",JsonValue.Create(toLine));json.Set(a.Token,"Index",JsonValue.Create(to));}
        if(b!=null){json.Set(b.Token,"Line",JsonValue.Create(fromLine));json.Set(b.Token,"Index",JsonValue.Create(from));}
    }
    public static string MovePoolEntry(string raw,int from,int to)
    {var tokens=Tokens(raw);if(from<0||from>=tokens.Count||to<0||to>=tokens.Count)throw new InvalidOperationException("Pool entry moved; refresh the board.");var entry=tokens[from];tokens.RemoveAt(from);tokens.Insert(to,entry);return string.Join(",",tokens);}
    public static string RemovePoolEntry(string raw,int index)
    {var tokens=Tokens(raw);if(index<0||index>=tokens.Count)throw new InvalidOperationException("Pool entry moved; refresh the board.");tokens.RemoveAt(index);return string.Join(",",tokens);}
    public static List<string> Tokens(string raw)=>raw.Length==0?[]:raw.Split(',').ToList();
}
