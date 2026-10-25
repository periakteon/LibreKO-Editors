using System.Globalization;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public partial class NpcVisualView:VBoxContainer
{
    readonly EditorSession session;readonly SourceDocument document;readonly int generation;readonly CancellationTokenSource lifetime;readonly TaskCompletionSource ready=new();
    readonly OptionButton entries,clips;readonly LineEdit filter,model;readonly VBoxContainer fields;readonly CodeEdit raw;readonly Label status,users;readonly PreviewViewport preview;
    readonly Dictionary<string,(LineEdit Input,string Initial)> inputs=[];readonly List<string> keys=[];JsonSourceDocument? json;string? group;int ordinal,page;bool rawDirty;WorldRowSnapshot[] prototypes=[];
    public Task ReadyTask=>ready.Task;public event Action<SourceFileKey>? SourceRequested;public PreviewViewport Preview=>preview;
    public string? SelectedModelStem=>string.IsNullOrWhiteSpace(model.Text)?null:model.Text;
    public NpcVisualView(EditorSession session,SourceDocument document)
    {
        this.session=session;this.document=document;generation=session.Sources.Generation;lifetime=CancellationTokenSource.CreateLinkedTokenSource(session.Sources.Cancellation);SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var bar=Ui.Add(this,new HFlowContainer());model=Ui.Field(bar,"Model / visual stem","Selected loose model");Ui.Button(bar,"Choose model…",()=>Pick("model_stem",v=>{model.Text=v.ToString();_=LoadPreview();}));Ui.Button(bar,"Load source visual",()=>_ = LoadPreview());Ui.Button(bar,"Refresh users",()=>_ = LoadUsers());Ui.Button(bar,"Clone visual…",CloneDialog);
        var sibling=Ui.Add(bar,new OptionButton());foreach(var suffix in new[]{"index.json","plug_slots.json","role_fx.json",".anim.json",".plugs.json",".fxplug.json",".glb"})sibling.AddItem(suffix);Ui.Button(bar,"Open mapping / metadata",()=>Run(()=>{var name=sibling.GetItemText(sibling.Selected);SourceRequested?.Invoke(Key("assets/npcs/"+(name.StartsWith('.')?model.Text+name:name)));}));
        users=Ui.Label(this,"Model IDs are client look IDs; NPC / monster identities stay distinct. Shared users are read from the selected source and optionally the connected database.",11,Ui.Muted);users.AutowrapMode=TextServer.AutowrapMode.WordSmart;users.MaxLinesVisible=4;
        var tabs=Ui.Add(this,new TabContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});var view=Ui.Column(tabs);view.Name="Source visual";view.SizeFlagsVertical=SizeFlags.ExpandFill;preview=Ui.Add(view,new PreviewViewport{CustomMinimumSize=new(280,200)});var clipBar=Ui.Row(view);clips=Ui.Add(clipBar,new OptionButton{FitToLongestItem=false,SizeFlagsHorizontal=SizeFlags.ExpandFill});clips.ItemSelected+=i=>preview.Play(clips.GetItemText((int)i));Ui.Button(clipBar,"Reset view",preview.ResetView);
        var structured=Ui.Column(tabs);structured.Name="Mappings & metadata";var selector=Ui.Add(structured,new HFlowContainer());filter=Ui.Input(selector,"Filter ID / name / clip");filter.TextChanged+=_=>Run(()=>{Flush();page=0;Choices();});entries=Ui.Add(selector,new OptionButton{CustomMinimumSize=new(300,28),FitToLongestItem=false});entries.ItemSelected+=i=>Run(()=>{Flush();ordinal=page*100+(int)i;ShowFields();});Ui.Button(selector,"‹",()=>Run(()=>{Flush();page=Math.Max(0,page-1);Choices();}));Ui.Button(selector,"›",()=>Run(()=>{Flush();if((page+1)*100<keys.Count){page++;Choices();}}));
        Ui.Button(selector,"Add entry…",AddDialog);Ui.Button(selector,"Duplicate",()=>Run(()=>Structure("duplicate")));Ui.Button(selector,"Remove",()=>Run(()=>Structure("remove")));Ui.Button(selector,"↑",()=>Run(()=>Structure("up")));Ui.Button(selector,"↓",()=>Run(()=>Structure("down")));
        var scroll=Ui.Add(structured,new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled});fields=Ui.Column(scroll);fields.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        raw=Ui.Add(tabs,new CodeEdit{Name="Raw source",GuttersDrawLineNumbers=true,SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});if(NpcVisualCatalog.Accepts(document.Snapshot.Key)){raw.Text=document.Text;raw.TextChanged+=()=>rawDirty=true;}else{raw.Editable=false;raw.Text="Binary source · "+document.Snapshot.Key.RelativePath+"\nUse reviewed compatible asset import for replacements.";}
        tabs.TabChanged+=i=>Run(()=>{Flush();if(i==2&&json!=null)raw.Text=document.Text;else if(i==1){Read();Choices();}});
        status=Ui.Label(this,"",11,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=4;
        var name=Path.GetFileName(document.Snapshot.Key.RelativePath);var metadataSuffix=new[]{".anim.json",".plugs.json",".fxplug.json"}.FirstOrDefault(s=>name.EndsWith(s,StringComparison.Ordinal));if(metadataSuffix!=null)model.Text=name[..^metadataSuffix.Length];else if(AssetDependencyAudit.IsModel(document.Snapshot.Key))model.Text=Path.GetFileNameWithoutExtension(name);
        if(name.EndsWith(".png",StringComparison.Ordinal)){preview.Visible=false;using var image=new Image();if(image.LoadPngFromBuffer(document.Render())==Error.Ok)Ui.Add(view,new TextureRect{Texture=ImageTexture.CreateFromImage(image),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});}
        try{Read();Choices();}catch(Exception e){status.Text=e.Message;tabs.CurrentTab=2;}
        session.Sources.Drafts!.Changed+=Stale;
    }
    public override async void _Ready(){try{await LoadSourceUsers();if(model.Text.Length>0)await LoadPreview();}finally{ready.TrySetResult();}}
    SourceFileKey Key(string relative)=>session.Sources.Store!.Key(SourceRoot.ClientProject,relative);
    void Ensure(){if(generation!=session.Sources.Generation||lifetime.IsCancellationRequested)throw new InvalidOperationException("NPC source context changed.");}
    void Run(Action action){try{Ensure();action();}catch(Exception e){status.Text=e.Message;}}
    void Read()
    {
        json=null;if(!NpcVisualCatalog.Accepts(document.Snapshot.Key))return;var parsed=document.Json();var root=parsed.Root.AsObject();json=parsed;group=root.ContainsKey("animations")?"animations":root.ContainsKey("plugs")?"plugs":root.ContainsKey("parts")?"parts":null;
    }
    void Choices()
    {
        keys.Clear();entries.Clear();if(json==null){Ui.Clear(fields);return;}var root=json.Root.AsObject();if(group!=null){var array=root[group]!.AsArray();for(int i=0;i<array.Count;i++)if((i+" "+array[i]).Contains(filter.Text,StringComparison.OrdinalIgnoreCase))keys.Add(i.ToString(CultureInfo.InvariantCulture));}
        else foreach(var pair in root)if((pair.Key+" "+pair.Value).Contains(filter.Text,StringComparison.OrdinalIgnoreCase))keys.Add(pair.Key);
        page=Math.Clamp(page,0,Math.Max(0,(keys.Count-1)/100));ordinal=Math.Clamp(ordinal,page*100,Math.Min(Math.Max(0,keys.Count-1),(page+1)*100-1));for(int i=page*100;i<Math.Min(keys.Count,(page+1)*100);i++){var value=group==null?root[keys[i]]:root[group]![int.Parse(keys[i],CultureInfo.InvariantCulture)];var text=value is JsonObject obj?(obj["exported"]??obj["model"]??obj["fx"]??obj["name"])?.ToString():value?.ToJsonString();entries.AddItem(keys[i]+" · "+text);}
        if(keys.Count>0)entries.Select(ordinal-page*100);ShowFields();
    }
    void ShowFields()
    {
        inputs.Clear();Ui.Clear(fields);if(json==null||keys.Count==0)return;var root=json.Root.AsObject();var key=keys[ordinal];JsonNode? current=group==null?root[key]:root[group]![int.Parse(key,CultureInfo.InvariantCulture)];Ui.Heading(fields,(group??"Mapping")+" · "+key,14);
        var values=current is JsonObject obj?obj:new JsonObject{["value"]=current?.DeepClone()};foreach(var pair in values){string text=pair.Value?.ToJsonString()??"null";string label=Ui.FieldName(pair.Key);if(pair.Key is "bone" or "joint"&&int.TryParse(pair.Value?.ToString(),out int index)&&preview.Model!=null&&NpcPreviewBuilder.Find<Skeleton3D>(preview.Model) is {} skeleton&&index>=0&&index<skeleton.GetBoneCount())label+=" · "+skeleton.GetBoneName(index);var input=Ui.Field(fields,label,pair.Key,text);inputs[pair.Key]=(input,text);input.FocusExited+=()=>Run(Flush);if(pair.Key is "bone" or "joint" or "model" or "fx" or "exported" or "anim"||pair.Key=="value"&&Path.GetFileName(document.Snapshot.Key.RelativePath)=="index.json"){var target=pair.Key;Ui.Button(input.GetParent(),"Choose…",()=>Pick(target,v=>Run(()=>{Flush();Set(target,v);Commit();})));}}
        Ui.Label(fields,"Values use JSON syntax. Editing metadata retains animation events, transforms and unknown properties. Use the raw source tab for additional custom fields.",11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        if(group==null&&Path.GetFileName(document.Snapshot.Key.RelativePath)=="index.json"&&current is JsonValue value&&value.TryGetValue<string>(out var stem)){model.Text=stem;DescribeUsers(stem);}
    }
    public void Flush()
    {
        Ensure();if(rawDirty){document.SetText(raw.Text);rawDirty=false;inputs.Clear();try{Read();}catch(Exception e){status.Text=e.Message;}return;}if(json==null||keys.Count==0)return;bool changed=false;foreach(var pair in inputs.ToArray())if(pair.Value.Input.Text!=pair.Value.Initial){JsonNode? value;try{value=JsonNode.Parse(pair.Value.Input.Text);}catch{value=JsonValue.Create(pair.Value.Input.Text);}Set(pair.Key,value);inputs[pair.Key]=(pair.Value.Input,pair.Value.Input.Text);changed=true;}if(changed)document.EditBytes(json.Render());
    }
    void Set(string field,JsonNode? value)
    {
        var root=json!.RowAt(0);string key=keys[ordinal];if(group==null){if(field=="value")json.Set(root.Token,key,value);else{var obj=root.Values[key]!.AsObject();obj[field]=value?.DeepClone();json.Set(root.Token,key,obj);}}
        else{var array=root.Values[group]!.AsArray();array[int.Parse(key,CultureInfo.InvariantCulture)]![field]=value?.DeepClone();json.Set(root.Token,group,array);}
    }
    public void SetMapping(int id,string stem){Ensure();Flush();var doc=NpcVisualDocument.OpenDraft(document.Snapshot,document.Render());doc.SetMapping(id,stem);document.EditBytes(doc.Render());Read();Choices();}
    public void SetNested(string field,JsonNode? value){Ensure();Flush();Set(field,value);Commit();}
    void Commit(){document.EditBytes(json!.Render());raw.Text=document.Text;rawDirty=false;Read();Choices();}
    void Structure(string action)
    {
        Flush();if(json==null||group==null||keys.Count==0)throw new InvalidOperationException("Ordered actions apply to animation, plug and FX-part arrays. Object mappings use named entries.");var root=json.RowAt(0);var array=root.Values[group]!.AsArray();int at=int.Parse(keys[ordinal],CultureInfo.InvariantCulture);if(action=="duplicate")array.Insert(at+1,array[at]!.DeepClone());else if(action=="remove")array.RemoveAt(at);else{int next=at+(action=="up"?-1:1);if(next<0||next>=array.Count)return;var value=array[at];array.RemoveAt(at);array.Insert(next,value);}json.Set(root.Token,group,array);Commit();
    }
    void AddDialog()
    {
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="New visual entry",DialogHideOnOk=false,OkButtonText="Prepare entry",MinSize=new(650,260)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(610,160);var name=Ui.Field(body,"ID / property name (object mappings)","100");var value=Ui.Field(body,"JSON value / full array record","\"model_stem\" / {…}");var error=Ui.Label(body,"",11,Ui.Accent);d.Confirmed+=()=>{try{Flush();if(json==null)throw new InvalidDataException("Open a JSON visual source.");var root=json.RowAt(0);var node=JsonNode.Parse(value.Text);if(group==null){if(root.Values.ContainsKey(name.Text))throw new InvalidDataException("Property exists; edit it explicitly.");json.Set(root.Token,name.Text,node);}else{var array=root.Values[group]!.AsArray();array.Add(node);json.Set(root.Token,group,array);}Commit();d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
    public async Task LoadUsers()
    {
        try{Ensure();if(!session.Connected){await LoadSourceUsers();return;}var all=new List<WorldRowSnapshot>();for(int p=0;p<200;p++){var batch=await session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.Npcs,Page:p,Size:100),lifetime.Token);Ensure();all.AddRange(batch.Rows);if(all.Count>=batch.Total||batch.Rows.Count==0)break;}prototypes=all.ToArray();DescribeUsers(model.Text);if(prototypes.Length>=20000)users.Text+=" · database scan capped at20,000; source model IDs remain visible";}catch(Exception e){if(IsInsideTree())users.Text=e.Message;}
    }
    async Task LoadSourceUsers()
    {
        await Task.CompletedTask;try{var store=session.Sources.Store!;var s=store.Read(store.Key(SourceRoot.ServerData,"Npcs.json"));if(s.Content!=null&&JsonNode.Parse(TextDocument.Decode(store.Blobs.Read(s.Content)).Text) is JsonArray array)prototypes=array.OfType<JsonObject>().Select(row=>new WorldRowSnapshot(new(WorldTable.Npcs,int.Parse(row["Id"]!.ToString(),CultureInfo.InvariantCulture),IsMonster:bool.TryParse(row["IsMonster"]?.ToString(),out var monster)?monster:row["IsMonster"]?.ToString()=="1"),row,"source")).ToArray();DescribeUsers(model.Text);}catch(Exception e){users.Text="Source users unavailable · "+e.Message;}
    }
    void DescribeUsers(string stem)
    {
        try{var indexKey=Key("assets/npcs/index.json");var source=document.Snapshot.Key==indexKey?document:session.Sources.Drafts!.Open(indexKey);var doc=NpcVisualDocument.OpenDraft(source.Snapshot,source.Render());var found=doc.SharedUsers(stem,prototypes);var ids=doc.Json.Root.AsObject().Where(p=>int.TryParse(p.Key,out _)&&p.Value?.ToString()==stem).Select(p=>p.Key);users.Text="Shared model IDs: "+string.Join(", ",ids)+" · "+found.Count+" known prototypes: "+string.Join(" · ",found.Take(30).Select(r=>(r.Key.IsMonster==true?"Monster ":"NPC ")+r.Key.Id+" · "+r.Values["Name"]))+(found.Count>30?" …":"");}catch(Exception e){users.Text="Shared user lookup unavailable · "+e.Message;}
    }
    public async Task LoadPreview()
    {
        try
        {
            Ensure();Flush();var store=session.Sources.Store!;var key=AssetDependencyAudit.IsModel(document.Snapshot.Key)?document.Snapshot.Key:Key("assets/npcs/"+NpcVisualCatalog.Stem(JsonValue.Create(model.Text))+".glb");var source=key==document.Snapshot.Key?document:session.Sources.Drafts!.Open(key);var candidate=source.Capture(store).After!;var overlay=session.Sources.CaptureDrafts()?.Files.ToDictionary(f=>f.Before.Key,f=>f.After);var issues=AssetDependencyAudit.Validate(store.Profile,key,candidate,store,overlay);if(issues.Count>0)throw new InvalidDataException(string.Join("; ",issues.Select(i=>i.Message)));
            var data=store.Blobs.Read(candidate);var info=GltfAssetDocument.Read(data,key.RelativePath.EndsWith(".glb",StringComparison.Ordinal));var relative="source-previews/"+Guid.NewGuid().ToString("N");var temp=WorkspacePaths.ResolveOwnedPath(session.Profile.WorkspaceRoot,relative);Directory.CreateDirectory(temp);foreach(var uri in info.ExternalUris){var dependency=Key(Path.GetDirectoryName(key.RelativePath)!.Replace('\\','/')+"/"+uri);var blob=overlay!=null&&overlay.TryGetValue(dependency,out var incoming)?incoming:store.Read(dependency).Content;var path=WorkspacePaths.ResolveOwnedPath(temp,uri);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,store.Blobs.Read(blob??throw new InvalidDataException("Dependency was deleted.")));}
            Ensure();var body=NpcPreviewBuilder.BuildLoose(data,temp);preview.ShowModel(body);preview.Visible=true;clips.Clear();foreach(var clip in preview.Animation?.GetAnimationList()??[])clips.AddItem(clip);if(clips.ItemCount>0)preview.Play(clips.GetItemText(0));status.Text="Loose source geometry · "+key.RelativePath+" · "+candidate.Sha256[..16]+" · "+info.BoneCount+" source joints · "+clips.ItemCount+" clips · attachment/effect output is verified in fresh source preview";DescribeUsers(model.Text);await Task.CompletedTask;
        }catch(Exception e){if(IsInsideTree())status.Text="Source visual unavailable · "+e.Message;}
    }
    void CloneDialog()
    {
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="Clone NPC visual sources",DialogHideOnOk=false,OkButtonText="Prepare independent visual",MinSize=new(650,280)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(610,180);var name=Ui.Field(body,"New model stem","unique_stem");var error=Ui.Label(body,"Copies the loose GLB and metadata to new paths. Referenced textures/plugs/effects remain shared and are shown in review. Remap selected model IDs explicitly.",12,Ui.Muted);error.AutowrapMode=TextServer.AutowrapMode.WordSmart;d.Confirmed+=()=>{try{Flush();var index=session.Sources.Drafts!.Open(Key("assets/npcs/index.json"));var change=NpcVisualDocument.OpenDraft(index.Snapshot,index.Render()).Clone(model.Text,name.Text,session.Sources.Store!,session.Sources.CaptureDrafts()?.Files.ToDictionary(f=>f.Before.Key,f=>f.After));CandidatesRequested?.Invoke(change);d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
    public event Action<FileChange>? CandidatesRequested;
    void Pick(string field,Action<JsonNode> chosen)
    {
        var d=Ui.Add(session.Shell,new AcceptDialog{Title="Select "+field,MinSize=new(600,510)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(560,410);var search=Ui.Input(body,"Filter resource / bone / clip");var list=Ui.Add(body,new ItemList{CustomMinimumSize=new(550,320)});var values=new List<(string Name,JsonNode Value)>();
        try
        {
            if(field is "bone" or "joint"){if(preview.Model==null||NpcPreviewBuilder.Find<Skeleton3D>(preview.Model) is not {} skeleton)throw new InvalidOperationException("Load the compatible source skeleton first.");for(int i=0;i<skeleton.GetBoneCount();i++)values.Add((i+" · "+skeleton.GetBoneName(i),JsonValue.Create(i)!));if(field=="bone")values.Insert(0,("-1 · body root",JsonValue.Create(-1)!));}
            else if(field is "anim" or "exported"){foreach(var clip in preview.Animation?.GetAnimationList()??[])values.Add((clip,JsonValue.Create(clip)!));if(field=="anim")values.Insert(0,("No plug animation",JsonValue.Create("")!));}
            else{string dir=field=="fx"?"assets/fx":field=="model"?"assets/npcs/plugs":"assets/npcs";string pattern=field=="fx"?"*.json":"*.glb";var path=Path.Combine(session.Sources.Profile!.Roots[SourceRoot.ClientProject],dir);SourcePaths.AssertNoLinks(path);if(Directory.Exists(path))foreach(var file in Directory.EnumerateFiles(path,pattern,new EnumerationOptions{AttributesToSkip=FileAttributes.ReparsePoint}).Order()){var stem=Path.GetFileNameWithoutExtension(file);values.Add((stem,JsonValue.Create(stem)!));if(values.Count>=10000)break;}foreach(var draft in session.Sources.Drafts!.Documents.Where(x=>!x.IsDeleted&&x.Snapshot.Key.RelativePath.StartsWith(dir+"/",StringComparison.Ordinal)&&Path.GetExtension(x.Snapshot.Key.RelativePath)==(field=="fx"?".json":".glb"))){var stem=Path.GetFileNameWithoutExtension(draft.Snapshot.Key.RelativePath);if(!values.Any(v=>v.Name==stem))values.Add((stem,JsonValue.Create(stem)!));}}
            var visible=new List<(string Name,JsonNode Value)>();void Refresh(){list.Clear();visible=values.Where(v=>v.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).Take(1000).ToList();foreach(var value in visible)list.AddItem(value.Name);}search.TextChanged+=_=>Refresh();list.ItemActivated+=i=>{chosen(visible[(int)i].Value);d.QueueFree();};Refresh();
        }catch(Exception e){Ui.Label(body,e.Message,12,Ui.Accent).AutowrapMode=TextServer.AutowrapMode.WordSmart;}d.Confirmed+=()=>d.QueueFree();d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
    public void SetActive(bool value){preview.ProcessMode=value?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;preview.GetChildren().OfType<SubViewport>().Single().RenderTargetUpdateMode=value?SubViewport.UpdateMode.Always:SubViewport.UpdateMode.Disabled;}
    void Stale(){if(IsInsideTree()){preview.Visible=false;status.Text="Source changed · load source geometry again or open a fresh captured source preview for attachments and effects.";}}
    public override void _ExitTree(){session.Sources.Drafts?.Changed-=Stale;lifetime.Cancel();lifetime.Dispose();}
}
