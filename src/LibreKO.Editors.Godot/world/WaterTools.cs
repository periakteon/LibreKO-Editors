using System.Globalization;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Core.Client;
namespace LibreKO.Editors.Desktop;
public static class WaterTools
{
    public static void Add(Control parent,Func<JsonObject> row,Action<string,JsonNode?> set,Label status)
    {
        var bar=Ui.Row(parent);var level=Ui.Field(bar,"Set water Y","finite level",row()["level_y"]?.ToString()??"0");
        Ui.Button(bar,"Apply level to grid",()=>{try{float y=float.Parse(level.Text,CultureInfo.InvariantCulture);if(!float.IsFinite(y))throw new InvalidDataException("Water level must be finite.");var verts=(JsonArray)row()["verts"]!.DeepClone();foreach(var point in verts)point!.AsArray()[1]=y;set("verts",verts);set("level_y",JsonValue.Create(y));}catch(Exception e){status.Text=e.Message;}});
    }
}
