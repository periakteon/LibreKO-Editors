using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.Core.References;
namespace LibreKO.Editors.Desktop;
public partial class SourceRowEditor:VBoxContainer
{
    readonly List<Action> pending=[];
    readonly List<Action> ready=[];
    public override void _Ready(){foreach(var resolve in ready)resolve();}
    public void Flush(){foreach(var apply in pending.ToArray())apply();}
    public SourceRowEditor(EditorSession session,SourceDocument source,JsonSourceDocument json,SourceJsonRow row)
    {
        int generation=session.Sources.Generation;var format=ServerJsonCatalog.Format(source.Snapshot.Key.RelativePath)!;var fields=ServerJsonCatalog.Fields(format,session.Sources.WorldCatalog);
        bool Current()=>IsInstanceValid(this)&&IsInsideTree()&&session.Sources.Generation==generation;
        var values=row.Values;
        foreach(var descriptor in fields)
        {
            var field=descriptor.Field;var spelling=values.Select(x=>x.Key).SingleOrDefault(x=>x.Equals(field.Name,StringComparison.OrdinalIgnoreCase))??field.Name;
            var group=Ui.Column(this);var title=descriptor.Label==field.Name?Ui.FieldName(field.Name):descriptor.Label;Ui.Label(group,title+(format.KeyFields.Contains(field.Name)?" · source key":""),11,Ui.Muted).TooltipText=field.Name;var original=values[spelling];
            Action refreshMeaning=()=>{};
            void Set(JsonNode? value){if(!Current()||!session.Operations.CanMutate)return;json.Set(row.Token,spelling,value);values[spelling]=value?.DeepClone();source.EditBytes(json.Render());refreshMeaning();}
            if(field.Kind==FieldKind.Boolean){var toggle=Ui.Add(group,new CheckButton{Name="SourceField_"+field.Name,Text=descriptor.Label,ButtonPressed=original?.ToString() is "true" or "1"});toggle.Toggled+=v=>Set(JsonValue.Create(v));continue;}
            var input=Ui.Input(group,field.Kind==FieldKind.Text?"Text":"Stored value",original?.ToString()??"");input.Name="SourceField_"+field.Name;
            input.TooltipText=field.Choices==null?field.Name: string.Join("\n",field.Choices.Select(p=>p.Key+" · "+p.Value));
            void Apply()
            {
                if(!Current())return;JsonNode? value;
                if(field.Kind==FieldKind.Text)value=JsonValue.Create(input.Text);
                else if(input.Text.Length==0&&descriptor.Nullable)value=null;
                else {try{value=JsonNode.Parse(input.Text);if(value is not JsonValue)value=JsonValue.Create(input.Text);}catch(System.Text.Json.JsonException){value=JsonValue.Create(input.Text);}}
                if(!values.ContainsKey(spelling)&&input.Text=="")return;
                if(values[spelling]?.ToString()==input.Text)return;Set(value);
            }
            input.TextChanged+=_=>Apply();input.FocusExited+=Apply;pending.Add(()=>{if(IsInstanceValid(input)&&input.HasFocus())Apply();});
            if(format.Table==WorldTable.MakeItemGroups&&field.Name=="Items")
            {var pool=Ui.Add(group,new OrderedItemPoolView(session,input.Text,text=>{input.Text=text;Apply();},Current));input.TextChanged+=text=>pool.SetRaw(text);}
            ReferenceIdentity? Reference()
            {
                if(!int.TryParse(input.Text,out var id))return null;
                if(format.Table is WorldTable table){try{return WorldRules.ReferenceFor(table,field.Name,ServerJsonCatalog.Normalize(values,fields,session.Sources.WorldCatalog?.Tables.Single(x=>x.Table==table).Defaults));}catch{return null;}}
                return field.Name switch{"NpcId"=>new("npcs",id),"ItemId" or "OriginItemNum" or "GiveItemNum"=>new("items",id),_=>field.Name.StartsWith("ExchangeItem",StringComparison.Ordinal)?new("items",id):null};
            }
            var meaning=Ui.Label(group,"",11,Ui.Accent);meaning.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            void Resolve(){var reference=Reference();meaning.Visible=reference!=null;meaning.Text=reference==null?"":reference.Id+" · resolving…";session.ReferencePresenter.Bind(meaning,Reference,Current,n=>meaning.Text=ReferenceLabels.Display(n)+" · "+n.Source,session.Sources.Profile?.Id);}
            refreshMeaning=Resolve;ready.Add(Resolve);input.TextChanged+=_=>Resolve();
            if(field.Choices!=null){var choices=Ui.Add(group,new OptionButton{FitToLongestItem=false,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});foreach(var item in field.Choices)choices.AddItem(item.Key+" · "+item.Value);choices.ItemSelected+=i=>{input.Text=field.Choices.Keys.ElementAt((int)i);Apply();};}
        }
        var unknown=values.Where(v=>!fields.Any(f=>f.Field.Name.Equals(v.Key,StringComparison.OrdinalIgnoreCase))).Select(x=>x.Key).ToArray();
        Ui.Label(this,unknown.Length==0?"Unknown fields are retained in the raw source.":"Preserved custom fields · "+string.Join(", ",unknown),11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
    }
}
