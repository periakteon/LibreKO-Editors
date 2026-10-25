using Godot;
using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public partial class SmdTools:VBoxContainer
{
    AcceptDialog? palette;
    SmdMapLayers? source;
    Action? onChanged;
    public SmdTools(EditorSession session,SmdMapLayers layers)
    {
        source=layers;
        var compact=Ui.Add(this,new HFlowContainer());var edit=Ui.Add(compact,new CheckButton{Text="Edit SMD",ButtonPressed=layers.Enabled});edit.Toggled+=v=>layers.Enabled=v;
        palette=Ui.Add(session.Shell,new AcceptDialog{Title="Server map source tools",MinSize=new(800,700)});var scroll=Ui.Add(palette,new ScrollContainer{Position=new(18,42),CustomMinimumSize=new(760,570),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled});var body=Ui.Column(scroll);body.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        palette.Confirmed+=()=>palette.Hide();palette.Canceled+=()=>palette.Hide();Ui.Button(compact,"SMD tools…",()=>palette.PopupCentered());
        VisibilityChanged+=()=>{if(!IsVisibleInTree())palette.Hide();};
        var bar=Ui.Add(body,new HFlowContainer());
        var mode=Ui.Add(bar,new OptionButton());mode.AddItem("Move source record");mode.AddItem("Paint blocked (0)");mode.AddItem("Paint walkable (1)");mode.ItemSelected+=i=>layers.PaintValue=i==0?null:(short)(i==1?0:1);
        Ui.Button(bar,"Inspect source…",()=>SmdInspector.Show(session,layers));Ui.Button(bar,"Undo source",layers.Undo);Ui.Button(bar,"Redo source",layers.Redo);
        var structural=Ui.Add(body,new HFlowContainer());var section=Ui.Add(structural,new OptionButton());foreach(var kind in new[]{"Warps","Regene","Objects"})section.AddItem(kind);
        var error=Ui.Label(body,"",11,Ui.Accent);error.Name="SmdToolsError";error.AutowrapMode=TextServer.AutowrapMode.WordSmart;foreach(var action in new[]{"Add","Duplicate","Remove","Up","Down"}){var value=action;Ui.Button(structural,action,()=>{try{layers.Structure(section.GetItemText(section.Selected),value);error.Text="";}catch(Exception e){error.Text=e.Message;}});}
        var heights=Ui.Row(body);var hx=Ui.Field(heights,"Server grid X","X","0");hx.Name="SmdHeightX";var hz=Ui.Field(heights,"Server grid Z","Z","0");hz.Name="SmdHeightZ";var hv=Ui.Field(heights,"Exact server height","Height","0");hv.Name="SmdHeightValue";
        Ui.Button(body,"Set server height",()=>Run(()=>layers.SetHeight(int.Parse(hx.Text,CultureInfo.InvariantCulture),int.Parse(hz.Text,CultureInfo.InvariantCulture),float.Parse(hv.Text,CultureInfo.InvariantCulture))));
        Ui.Label(body,"Server-only height editing preserves client terrain. The map preview refreshes from this SMD draft; linked client changes require an explicit terrain alignment review.",11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        Ui.Button(body,"Import verified collision section…",ImportCollision);
        var collision=Ui.Row(body);var triangles=Ui.Field(collision,"Collision triangle IDs","Comma separated zero-based IDs","0");var delta=Ui.Field(collision,"Translate XYZ","Map units","0 0 0");
        var provenance=Ui.Add(body,new TextEdit{CustomMinimumSize=new(0,90),PlaceholderText="{\"sourceSha256\":\"original SMD hash\",\"shapes\":{\"27\":[0]}}"});var confirmed=Ui.Add(body,new CheckButton{Text="I verified shape-to-face provenance against this original SMD"});
        Ui.Button(body,"Transform verified collision region",()=>Run(()=>
        {
            if(!confirmed.ButtonPressed)throw new InvalidDataException("Verify shape-to-face provenance before rebuilding server collision indexes.");var proof=JsonNode.Parse(provenance.Text)!.AsObject();if(!string.Equals(proof["sourceSha256"]?.GetValue<string>(),layers.Snapshot.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Provenance must identify this exact original SMD hash.");var shapes=proof["shapes"]!.AsObject().ToDictionary(p=>int.Parse(p.Key,CultureInfo.InvariantCulture),p=>(IReadOnlyList<int>)p.Value!.AsArray().Select(v=>v!.GetValue<int>()).ToArray());var ids=triangles.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(v=>int.Parse(v,CultureInfo.InvariantCulture)).ToArray();var xyz=delta.Text.Split([' ',','],StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();if(xyz.Length!=3)throw new InvalidDataException("Enter three finite translation components.");layers.TransformCollision(ids,new(xyz[0],xyz[1],xyz[2]),shapes);
        }));
        void Run(Action action){try{action();error.Text="";}catch(Exception e){error.Text=e.Message;}}
        void ImportCollision()
        {
            palette.Hide();var picker=Ui.Add(session.Shell,new FileDialog{Name="SmdCollisionSectionImport",Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,Filters=["*.smd ; Supported complete SMD"],Title="Import verified SMD collision section"});picker.FileSelected+=path=>{picker.Hide();picker.QueueFree();Run(()=>
            {
                SourcePaths.AssertNoLinks(path);using var input=File.OpenRead(path);var bytes=new byte[SourceLimits.CheckedLength(input.Length)];input.ReadExactly(bytes);var imported=SmdCodec.Read(bytes);if(!imported.Complete||SmdValidation.Validate(imported).Count>0)throw new InvalidDataException("Select a complete valid SMD. Only its verified collision section will be imported.");var review=Ui.Add(session.Shell,new ConfirmationDialog{Title="Review SMD collision section import",DialogText=path+"\nSHA256 "+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))+"\n"+imported.CollisionVertices.Length/9+" collision faces · dimensions "+imported.MapWidth+" × "+imported.MapHeight+"\nOnly collision changes; heights, tiles, records and tail remain untouched.",OkButtonText="Prepare section draft",MinSize=new(730,300)});review.Confirmed+=()=>{review.Hide();Run(()=>layers.Edit(d=>SmdCollisionIndex.ImportSection(d,imported)));review.QueueFree();};review.Canceled+=()=>{review.Hide();review.QueueFree();};review.PopupCentered();
            });};picker.Canceled+=()=>picker.QueueFree();picker.PopupCenteredRatio(.7f);
        }
        var list=Ui.Add(body,new OptionButton{SizeFlagsHorizontal=SizeFlags.ExpandFill,FitToLongestItem=false});var entries=new List<(string,int)>();
        var status=Ui.Label(body,"",11,Ui.Muted);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=8;
        void Refresh()
        {
            list.Clear();entries.Clear();var doc=layers.Document;
            for(int i=0;i<doc.Warps.Count;i++){list.AddItem($"Warp row{i} · {doc.Warps[i].Id} · {doc.Warps[i].Name}");entries.Add(("Warps",i));}
            for(int i=0;i<doc.Regene.Count;i++){list.AddItem($"Regene row{i}");entries.Add(("Regene",i));}
            for(int i=0;i<doc.Objects.Count;i++){list.AddItem($"Object row{i} · {doc.Objects[i].Index} · Type {doc.Objects[i].Type}");entries.Add(("Objects",i));}
            var selected=entries.IndexOf((layers.SelectedKind,layers.SelectedOrdinal));if(selected>=0)list.Select(selected);
            status.Text=(layers.Draft.IsDirty?"Unsaved source draft · ":"Source snapshot · ")+layers.Snapshot.Key.RelativePath+"\nSHA256 "+layers.Snapshot.Sha256+"\nServer source data · overlay budget 2,000 records / 4,096 blocked cells / 16,000 collision triangles · "+string.Join(" · ",doc.Diagnostics);
        }
        list.ItemSelected+=i=>{if(i>=0&&i<entries.Count)layers.Select(entries[(int)i].Item1,entries[(int)i].Item2);};onChanged=Refresh;layers.Changed+=Refresh;Refresh();
    }
    public override void _ExitTree(){if(source!=null&&onChanged!=null)source.Changed-=onChanged;if(palette!=null&&IsInstanceValid(palette))palette.QueueFree();}
}
