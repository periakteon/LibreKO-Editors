using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public static class WorldPatrolEditor
{
    public static void Show(EditorSession session,WorldRowSnapshot row,Action<WorldRowChange> apply)
    {
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="Patrol path · spawn "+row.Key.Id,MinSize=new(680,620),OkButtonText="Apply path to draft",DialogHideOnOk=false});
        var body=Ui.Column(d);body.Position=new(16,40);body.CustomMinimumSize=new(640,510);
        Ui.Label(body,"Absolute map X/Z, one waypoint per line: X, Z. Reorder lines to reorder the path. One Apply is one undo step. Movement mode and raw direction remain separate fields.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var input=Ui.Add(body,new TextEdit{CustomMinimumSize=new(630,330),WrapMode=TextEdit.LineWrappingMode.None});var status=Ui.Label(body,"",12,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        bool malformed=false;try {input.Text=string.Join("\n",PatrolPath.Decode((byte)FieldValueParser.ReadInt32(row.Values["DotCnt"]),row.Values["Path"]?.ToString()).Select(p=>p.X+", "+p.Z));}
        catch(Exception e){malformed=true;input.Editable=false;input.Text=row.Values["Path"]?.ToString()??"";status.Text="Malformed legacy path preserved · "+e.Message;}
        Ui.Button(body,"Repair / start a new path",()=>{malformed=false;input.Editable=true;input.Text="";status.Text="A new path will replace the legacy path only when you apply and save.";});
        var generation=session.Generation;var scope=session.HistoryRoot;
        d.Confirmed+=()=>
        {
            if(malformed){status.Text="Choose the explicit repair action first.";return;}
            try
            {
                if(generation!=session.Generation||scope!=session.HistoryRoot)throw new InvalidOperationException("Database scope changed.");
                var points=input.Text.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(line=>{var pair=line.Split(',',StringSplitOptions.TrimEntries);if(pair.Length!=2)throw new FormatException("Each line requires X, Z.");return new MapPoint(float.Parse(pair[0],System.Globalization.CultureInfo.InvariantCulture),0,float.Parse(pair[1],System.Globalization.CultureInfo.InvariantCulture));}).ToArray();
                var encoded=PatrolPath.Encode(points);var values=(JsonObject)row.Values.DeepClone();values["DotCnt"]=encoded.Count;values["Path"]=encoded.Path;apply(new(row.Key,row,values));d.QueueFree();
            }
            catch(Exception e){status.Text=e.Message;}
        };
        d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
}
