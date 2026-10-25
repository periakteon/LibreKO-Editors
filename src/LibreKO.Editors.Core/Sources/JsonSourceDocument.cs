using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public record SourceRowToken(SourceFileKey File,string FileHash,string JsonPointer,string RowHash);
public record SourceJsonRow(SourceRowToken Token,JsonObject Values);

/// <summary>Original lexical spans survive edits, including unknown numbers, escapes and whitespace.</summary>
public sealed class JsonSourceDocument
{
    readonly TextDocument text;
    readonly byte[] original;
    readonly SourceFileSnapshot snapshot;
    readonly List<Row> originalRows=[];
    readonly List<Row> rows=[];
    readonly List<string> gaps=[];
    readonly string prefix, suffix, separator;
    readonly bool array;
    int nextNew;
    public SourceFileSnapshot Snapshot=>snapshot;
    public int RowCount=>rows.Count;
    public SourceJsonRow RowAt(int index)=>rows[index].Values is JsonObject obj?new(rows[index].Token,(JsonObject)obj.DeepClone()):throw new InvalidDataException("Expected an object row at this source ordinal.");
    public JsonNode Root=>JsonNode.Parse(TextDocument.Decode(Render()).Text,documentOptions:new(){MaxDepth=SourceLimits.MaximumJsonDepth})!;
    public IReadOnlyList<SourceJsonRow> Rows=>rows.Where(x=>x.Values is JsonObject).Select(x=>new SourceJsonRow(x.Token,(JsonObject)x.Values.DeepClone())).ToArray();
    public bool IsDirty=>!Render().AsSpan().SequenceEqual(original);

    JsonSourceDocument(SourceFileSnapshot snapshot,byte[] bytes,bool verify)
    {
        this.snapshot=snapshot; original=bytes.ToArray(); text=TextDocument.Decode(bytes);
        if(verify && snapshot.Exists && !string.Equals(snapshot.Sha256,Convert.ToHexString(SHA256.HashData(bytes)),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source bytes do not match the captured file hash.");
        var utf8=Encoding.UTF8.GetBytes(text.Text);
        try
        {
            // Count before creating per-row nodes; depth is bounded by the reader itself.
            var probe=new Utf8JsonReader(utf8,new JsonReaderOptions{MaxDepth=SourceLimits.MaximumJsonDepth}); int count=0;
            while(probe.Read()) if(probe.CurrentDepth==1 && probe.TokenType is not JsonTokenType.EndObject and not JsonTokenType.EndArray && ++count>SourceLimits.MaximumJsonRows) throw new InvalidDataException("JSON is limited to 1,000,000 top-level values.");
            var reader=new Utf8JsonReader(utf8,new JsonReaderOptions{MaxDepth=SourceLimits.MaximumJsonDepth}); reader.Read(); array=reader.TokenType==JsonTokenType.StartArray;
            if(!array && reader.TokenType!=JsonTokenType.StartObject) throw new InvalidDataException("Expected a JSON object or array.");
            var ranges=new List<(int Start,int End)>();
            if(array)
            {
                while(reader.Read() && reader.TokenType!=JsonTokenType.EndArray) { int start=checked((int)reader.TokenStartIndex); reader.Skip(); ranges.Add((start,checked((int)reader.BytesConsumed))); }
            }
            else { int start=checked((int)reader.TokenStartIndex); reader.Skip(); ranges.Add((start,checked((int)reader.BytesConsumed))); }
            prefix=Encoding.UTF8.GetString(utf8.AsSpan(0,ranges.Count==0 ? checked((int)reader.TokenStartIndex) : ranges[0].Start));
            suffix=Encoding.UTF8.GetString(utf8.AsSpan(ranges.Count==0 ? checked((int)reader.TokenStartIndex) : ranges[^1].End));
            separator=ranges.Count>1 ? Encoding.UTF8.GetString(utf8.AsSpan(ranges[0].End,ranges[1].Start-ranges[0].End)) : ","+text.Newline+"  ";
            for(int i=0;i<ranges.Count;i++)
            {
                if(i>0) gaps.Add(Encoding.UTF8.GetString(utf8.AsSpan(ranges[i-1].End,ranges[i].Start-ranges[i-1].End)));
                var raw=Encoding.UTF8.GetString(utf8.AsSpan(ranges[i].Start,ranges[i].End-ranges[i].Start));
                var token=new SourceRowToken(snapshot.Key,snapshot.Sha256,array?"/"+i:"",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))));
                var row=new Row(token,raw,JsonNode.Parse(raw)!); rows.Add(row); originalRows.Add(row);
            }
        }
        catch(JsonException e) { throw new InvalidDataException($"Invalid JSON at line {e.LineNumber}, byte {e.BytePositionInLine}: {e.Message}",e); }
    }
    public static JsonSourceDocument Open(SourceFileSnapshot snapshot,byte[] bytes)=>new(snapshot,bytes,true);
    public static JsonSourceDocument OpenDraft(SourceFileSnapshot reviewedBefore,byte[] candidate)=>new(reviewedBefore,candidate,false);
    Row Find(SourceRowToken token)=>rows.SingleOrDefault(x=>x.Token==token) ?? throw new InvalidOperationException("The row token belongs to another source, stale hash or removed row.");
    public JsonObject Values(SourceRowToken token)=>(JsonObject)Find(token).Values.DeepClone();
    public void Set(SourceRowToken token,string field,JsonNode? value)
    { var row=Find(token); if(row.Values is not JsonObject obj) throw new InvalidOperationException("Expected an object row."); obj[field]=value?.DeepClone(); }
    public void Insert(JsonObject value,int index)=>InsertValue(value,index);
    public void InsertValue(JsonNode value,int index)
    {
        if(!array || index<0 || index>rows.Count || rows.Count>=SourceLimits.MaximumJsonRows) throw new InvalidOperationException("Cannot insert at this JSON position.");
        var raw=value.ToJsonString(); rows.Insert(index,new(new(snapshot.Key,snapshot.Sha256,"/new/"+nextNew++,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))),raw,value.DeepClone()));
    }
    public void RemoveAt(int index) { if(!array) throw new InvalidOperationException("Root is not an array."); rows.RemoveAt(index); }
    public void Remove(SourceRowToken token) { if(!array) throw new InvalidOperationException("Cannot remove the root object."); rows.Remove(Find(token)); }
    public void Move(SourceRowToken token,int index)
    { var row=Find(token); if(!array || index<0 || index>=rows.Count) throw new InvalidOperationException("Invalid move position."); rows.Remove(row); rows.Insert(index,row); }
    public byte[] Render()
    {
        bool structure=rows.Count!=originalRows.Count || !rows.SequenceEqual(originalRows);
        if(!structure && rows.All(x=>JsonNode.DeepEquals(x.Values,x.Original))) return original.ToArray();
        if(!array) return text.Encode(prefix+rows[0].Render()+suffix);
        if(!structure)
        {
            var result=new StringBuilder(prefix);
            for(int i=0;i<rows.Count;i++) { if(i>0) result.Append(gaps[i-1]); result.Append(rows[i].Render()); }
            return text.Encode(result.Append(suffix).ToString());
        }
        return text.Encode(prefix+string.Join(separator,rows.Select(x=>x.Render()))+suffix);
    }
    sealed class Row(SourceRowToken token,string raw,JsonNode values)
    {
        public SourceRowToken Token{get;}=token;
        public JsonNode Values{get;}=values;
        public JsonNode Original{get;}=values.DeepClone();
        public string Render()
        {
            if(JsonNode.DeepEquals(Values,Original)) return raw;
            if(Values is not JsonObject obj || Original is not JsonObject before) return Values.ToJsonString();
            var bytes=Encoding.UTF8.GetBytes(raw); var reader=new Utf8JsonReader(bytes); reader.Read();
            var patches=new List<(int Start,int End,string Value)>(); var seen=new HashSet<string>();
            while(reader.Read() && reader.TokenType!=JsonTokenType.EndObject)
            {
                string name=reader.GetString()!; if(!seen.Add(name)) throw new InvalidDataException("Duplicate JSON property names cannot be edited safely."); reader.Read(); int start=(int)reader.TokenStartIndex; reader.Skip(); int end=(int)reader.BytesConsumed;
                if(!JsonNode.DeepEquals(obj[name],before[name])) patches.Add((start,end,obj[name]?.ToJsonString()??"null"));
            }
            // Set does not delete properties: null is an explicit JSON null.
            var added=obj.Where(x=>!seen.Contains(x.Key)).Select(x=>JsonSerializer.Serialize(x.Key)+":"+(x.Value?.ToJsonString()??"null")).ToArray();
            if(added.Length>0) { int close=(int)reader.TokenStartIndex; patches.Add((close,close,(seen.Count>0?",":"")+string.Join(",",added))); }
            var result=new StringBuilder(); int offset=0;
            foreach(var p in patches.OrderBy(x=>x.Start)) { result.Append(Encoding.UTF8.GetString(bytes.AsSpan(offset,p.Start-offset))).Append(p.Value); offset=p.End; }
            return result.Append(Encoding.UTF8.GetString(bytes.AsSpan(offset))).ToString();
        }
    }
}
