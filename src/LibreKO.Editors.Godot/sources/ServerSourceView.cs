using System.Text;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Client;
namespace LibreKO.Editors.Desktop;
public partial class ServerSourceView:VBoxContainer
{
    readonly EditorSession session;readonly int generation;
    readonly Dictionary<SourceFileKey,Entry> entries=[];
    readonly ItemList library;readonly OptionButton roots,documents;readonly VBoxContainer host;readonly Label status;readonly LineEdit search;
    SourceFileKey[] files=[];Entry? selected;
    public Task PreviewReady{get;private set;}=Task.CompletedTask;
    public SourceDocument? SelectedDocument=>selected?.Document;
    public int SelectedRowOrdinal { get; private set; }
    public bool HasRowSelection=>selected?.RowCount>0;
    public string? SelectedModelStem=>selected?.Visual?.SelectedModelStem;
    public event Action? SelectionChanged;
    public event Action? ReturnRequested;
    public void SelectSourceRow(int ordinal){if(selected==null||!selected.ChooseRow(ordinal))throw new InvalidOperationException("The source row no longer exists.");SelectedRowOrdinal=ordinal;session.Shell.RecordLocation();}
    internal void ShowLibrary(){session.Sources.Previews.Invalidate();foreach(var entry in entries.Values)entry.Pane.Visible=false;selected=null;documents.Select(-1);status.Text="Choose a source snapshot";SelectedRowOrdinal=0;}
    internal void EditExternal(SourceFileKey key,Action<SourceDocument> edit)
    {
        if(entries.TryGetValue(key,out var entry)){entry.Flush();edit(entry.Document);Build(entry);}
        else edit(session.Sources.Drafts!.Open(key));
    }
    public ServerSourceView(EditorSession session)
    {
        this.session=session;generation=session.Sources.Generation;SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        Ui.Add(this,new ActivityIndicator(session.Loading,"source"));
        var top=Ui.Add(this,new HFlowContainer());Ui.Button(top,"New source…",NewSource);Ui.Button(top,"Import SMD…",ImportSmd);Ui.Button(top,"Import compatible asset…",ImportAsset);Ui.Button(top,"Delete source…",()=>_ = DeleteSelected());Ui.Button(top,"Include quest in manifest",()=>_ = SetMembership(true));Ui.Button(top,"Review all source changes",()=>_ = Review());Ui.Button(top,"File history…",()=>SourceHistoryView.Show(session));
        Ui.Button(top,"Close document",()=>_ = CloseSelectedAsync()).TooltipText="Close this source snapshot; dirty sources offer Review, Discard or Cancel.";
        Ui.Button(top,"Return to database",()=>ReturnRequested?.Invoke()).TooltipText="Return to the World database view. Open source drafts remain tracked until saved or discarded.";
        Ui.Label(this,"Selected source files · database and seeds are independent · code-backed zone policies remain in server Startup/World C# sources",11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var split=Ui.Add(this,new HSplitContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SplitOffsets=[260]});var left=Ui.Column(split);left.CustomMinimumSize=new(230,0);left.SizeFlagsHorizontal=Control.SizeFlags.Fill;
        roots=Ui.Add(left,new OptionButton());foreach(var root in new[]{SourceRoot.ServerData,SourceRoot.ServerMaps,SourceRoot.ServerQuests,SourceRoot.ClientProject})roots.AddItem(root.ToString(),(int)root);roots.ItemSelected+=_=>Reload();
        search=Ui.Input(left,"Filter source filename");search.TextChanged+=_=>Reload();library=Ui.Add(left,new ItemList{SizeFlagsVertical=SizeFlags.ExpandFill,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis});library.ItemSelected+=i=>{if(i>=0&&i<files.Length) _=SafeOpen(files[(int)i]);};
        var right=Ui.Column(split);right.SizeFlagsHorizontal=SizeFlags.ExpandFill;documents=Ui.Add(right,new OptionButton{FitToLongestItem=false,SizeFlagsHorizontal=SizeFlags.ExpandFill});documents.ItemSelected+=i=>Select(entries.Values.ElementAt((int)i));
        host=Ui.Column(right);host.SizeFlagsVertical=SizeFlags.ExpandFill;host.SizeFlagsHorizontal=SizeFlags.ExpandFill;status=Ui.Label(this,"Open a source snapshot. Unsupported files are read-only.",11,Ui.Muted);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=2;
        Reload();
    }
    bool Current()=>IsInstanceValid(this)&&IsInsideTree()&&generation==session.Sources.Generation;
    SourceFileKey Key(SourceRoot root,string path)=>session.Sources.Store!.Key(root,path);
    void Reload()
    {
        library.Clear();files=[];try
        {
            var root=(SourceRoot)roots.GetSelectedId();var profile=session.Sources.Profile;if(profile==null||!profile.Roots.TryGetValue(root,out var path)||!Directory.Exists(path))return;
            SourceIdentity.Root(path);var found=new List<SourceFileKey>();
            // Enumerate without following linked directories. Every opened path is guarded again.
            var scanRoot=root==SourceRoot.ClientProject?Path.Combine(path,"assets"):path;if(!Directory.Exists(scanRoot))return;
            foreach(var file in Directory.EnumerateFiles(scanRoot,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}))
            {var relative=Path.GetRelativePath(path,file).Replace('\\','/');if(!relative.Contains(search.Text,StringComparison.OrdinalIgnoreCase))continue;var key=Key(root,relative);if(root==SourceRoot.ClientProject&&!ClientTerrainCatalog.Accepts(key)&&!ClientMapCatalog.Accepts(key)&&!NpcVisualCatalog.Accepts(key)&&!VisualAsset(key))continue;found.Add(key);if(found.Count>10000)throw new InvalidOperationException("Narrow the file filter: more than 10,000 sources match.");}
            files=found.OrderBy(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase).ToArray();foreach(var file in files)library.AddItem(file.RelativePath);
        }
        catch(Exception e){if(status!=null)status.Text=e.Message;}
    }
    async Task SafeOpen(SourceFileKey key){try{await OpenAsync(key);}catch(Exception e){status.Text=e.Message;}}
    public Task OpenAsync(SourceFileKey key)
    {
        using var activity=session.Loading.Begin("source","Opening source snapshot…");
        if(generation!=session.Sources.Generation)throw new InvalidOperationException("Source profile changed; reopen the source workspace.");
        if(entries.TryGetValue(key,out var existing)){Select(existing);return Task.CompletedTask;}
        var store=session.Sources.Store??throw new InvalidOperationException("Select source roots first.");var snapshot=store.Read(key);if(!snapshot.Exists)throw new FileNotFoundException("Source is missing; use New source to create it explicitly.");
        Add(Authorized(key)?session.Sources.Drafts!.Open(key):new(snapshot,store.Blobs.Read(snapshot.Content!)));return Task.CompletedTask;
    }
    public void OpenCandidate(FileMutation mutation)
    {
        if(mutation.After==null)throw new InvalidOperationException("Use Delete source for removals.");
        if(entries.TryGetValue(mutation.Before.Key,out var existing)){if(existing.Document.Snapshot!=mutation.Before)throw new InvalidOperationException("File candidate has a stale baseline.");existing.Document.EditBytes(session.Sources.Blobs!.Read(mutation.After));Build(existing);Select(existing);}
        else {var document=session.Sources.Drafts?.Documents.SingleOrDefault(d=>d.Snapshot.Key==mutation.Before.Key)??new SourceDocument(mutation.Before,mutation.Before.Content==null?"[]"u8.ToArray():session.Sources.Blobs!.Read(mutation.Before.Content));if(document.Snapshot!=mutation.Before)throw new InvalidOperationException("File candidate has a stale dependency baseline.");document.EditBytes(session.Sources.Blobs!.Read(mutation.After));Add(document);}
    }
    void Add(SourceDocument document)
    {
        var entry=new Entry(document,Ui.Column(host));entries.Add(document.Snapshot.Key,entry);entry.Pane.SizeFlagsVertical=SizeFlags.ExpandFill;Build(entry);
        if(Authorized(document.Snapshot.Key))entry.Registration=session.Sources.Drafts!.Register(document,()=>entry.Flush(),()=>Reset(entry));
        document.Changed+=()=>{if(Current())RefreshTitles();};RefreshTitles();Select(entry);
    }
    static bool VisualAsset(SourceFileKey key)=>AssetDependencyAudit.Accepts(key)&&(key.RelativePath.StartsWith("assets/npcs/",StringComparison.Ordinal)||key.RelativePath.StartsWith("assets/objects/",StringComparison.Ordinal)||key.RelativePath.StartsWith("assets/fx/tex/",StringComparison.Ordinal));
    bool Authorized(SourceFileKey key)=>NpcVisualCatalog.Accepts(key)||VisualAsset(key)||ClientTerrainCatalog.Accepts(key)||ClientMapCatalog.Accepts(key)||ServerJsonCatalog.Accepts(key)||QuestManifest.Accepts(key)||key.Root==SourceRoot.ServerMaps&&key.RelativePath.EndsWith(".smd",StringComparison.OrdinalIgnoreCase);
    void Reset(Entry entry)
    {
        if(!entry.Document.Snapshot.Exists&&!entry.Document.IsDirty)
        {
            entries.Remove(entry.Document.Snapshot.Key);entry.Registration?.Dispose();session.Sources.Drafts?.ForgetMissing(entry.Document);entry.Lifetime.Cancel();entry.Lifetime.Dispose();host.RemoveChild(entry.Pane);entry.Pane.QueueFree();if(selected==entry)selected=null;documents.Clear();RefreshTitles();if(selected==null&&entries.Count>0)Select(entries.Values.First());return;
        }
        Build(entry);
    }
    void Select(Entry entry)
    {
        session.Shell.RememberContext();
        if(selected!=entry){session.Sources.Previews.Invalidate();SelectionChanged?.Invoke();}
        selected=entry;foreach(var e in entries.Values){e.Pane.Visible=e==entry;if(e.Preview!=null)e.Preview.SetActive(e==entry&&IsVisibleInTree());e.Terrain?.SetActive(e==entry&&IsVisibleInTree());e.Scenery?.SetActive(e==entry&&IsVisibleInTree());e.Collision?.SetActive(e==entry&&IsVisibleInTree());e.Visual?.SetActive(e==entry&&IsVisibleInTree());}
        if(roots.GetSelectedId()!=(int)entry.Document.Snapshot.Key.Root){for(int i=0;i<roots.ItemCount;i++)if(roots.GetItemId(i)==(int)entry.Document.Snapshot.Key.Root){roots.Select(i);Reload();break;}}
        library.DeselectAll();var fileIndex=Array.IndexOf(files,entry.Document.Snapshot.Key);if(fileIndex>=0)library.Select(fileIndex);
        documents.Select(entries.Values.ToList().IndexOf(entry));status.Text=entry.Document.Snapshot.Key.RelativePath+" · "+(entry.Document.IsDirty?"Unapplied draft":"Source snapshot");
        SelectedRowOrdinal=entry.Ordinal;session.Shell.RecordLocation();
    }
    public async Task<bool> CloseSelectedAsync()
    {
        if(selected==null||!session.Operations.CanMutate)return false;
        var entry=selected;entry.Flush();
        if(entry.Document.IsDirty)
        {
            var completion=new TaskCompletionSource<int>();
            var dialog=Ui.Add(session.Shell,new ConfirmationDialog{Title="Close source document",DialogText="This source has unapplied changes. Review them before closing?",OkButtonText="Review changes",CancelButtonText="Cancel"});
            dialog.AddButton("Discard this source",false,"discard");
            dialog.Confirmed+=()=>completion.TrySetResult(1);dialog.Canceled+=()=>completion.TrySetResult(0);dialog.CustomAction+=a=>{if(a=="discard")completion.TrySetResult(2);};dialog.PopupCentered();
            var choice=await completion.Task;dialog.Hide();dialog.QueueFree();if(choice==0)return false;
            if(choice==1){await SourceChangeReview.Show(session,null,new FileChange(Guid.NewGuid(),[entry.Document.Capture(session.Sources.Store!)]));if(entry.Document.IsDirty)return false;}
            else entry.Document.Discard();
        }
        if(!Current()||!entries.ContainsKey(entry.Document.Snapshot.Key))return true;
        session.Shell.RememberContext();session.Sources.Previews.Invalidate();
        entries.Remove(entry.Document.Snapshot.Key);entry.Registration?.Dispose();entry.Lifetime.Cancel();entry.Lifetime.Dispose();
        session.Sources.Drafts?.ForgetMissing(entry.Document);host.RemoveChild(entry.Pane);entry.Pane.QueueFree();selected=null;documents.Clear();RefreshTitles();
        if(entries.Count>0)Select(entries.Values.Last());else{status.Text="Source snapshot closed";ReturnRequested?.Invoke();}
        session.Shell.RecordLocation();return true;
    }
    void RefreshTitles(){int at=0;foreach(var e in entries.Values){var text=(e.Document.IsDirty?"● ":"")+e.Document.Snapshot.Key.RelativePath;if(at>=documents.ItemCount)documents.AddItem(text);else documents.SetItemText(at,text);if(e.Badge!=null&&IsInstanceValid(e.Badge))e.Badge.Text=Badge(e.Document);at++;}if(selected!=null)status.Text=selected.Document.Snapshot.Key.RelativePath+" · "+(selected.Document.IsDirty?"Unapplied draft":"Source snapshot");}
    static string Badge(SourceDocument document)=>(document.IsDeleted?"Pending deletion · ":document.IsDirty?"Unapplied source draft · ":"Source snapshot · ")+document.Snapshot.Key.Root+" · "+(document.Snapshot.Exists?document.Snapshot.Sha256[..Math.Min(16,document.Snapshot.Sha256.Length)]:"new file");
    void Build(Entry entry)
    {
        entry.Lifetime.Cancel();entry.Lifetime.Dispose();entry.Lifetime=new();entry.Preview=null;entry.Terrain=null;entry.Scenery=null;entry.Collision=null;entry.Visual=null;Ui.Clear(entry.Pane);entry.RowCount=0;entry.Flush=()=>{};var document=entry.Document;var key=document.Snapshot.Key;
        Ui.Heading(entry.Pane,key.RelativePath,16);entry.Badge=Ui.Label(entry.Pane,Badge(document),11,Ui.Accent);
        if(document.IsDeleted){Ui.Label(entry.Pane,"This source will be removed only after Apply. Its original bytes remain in immutable history.",12,Ui.Muted);return;}
        bool writable=Authorized(key);
        if(writable&&key.Root!=SourceRoot.ServerMaps&&!ClientTerrainCatalog.Accepts(key)&&!key.RelativePath.EndsWith("collision.bin",StringComparison.Ordinal)&&!VisualAsset(key))Ui.Button(entry.Pane,"Convert text encoding…",()=>ConvertEncoding(entry));
        if(NpcVisualCatalog.Accepts(key)||VisualAsset(key))
        {var visual=Ui.Add(entry.Pane,new NpcVisualView(session,document));entry.Visual=visual;entry.Flush=visual.Flush;PreviewReady=visual.ReadyTask;visual.SourceRequested+=k=>_ = SafeOpen(k);visual.CandidatesRequested+=change=>{foreach(var file in change.Files)OpenCandidate(file);};return;}
        if(ClientMapCatalog.Accepts(key))
        {
            if(key.RelativePath.EndsWith("collision.bin",StringComparison.Ordinal)){var collision=Ui.Add(entry.Pane,new CollisionTools(session,document));entry.Collision=collision;entry.Flush=collision.Flush;PreviewReady=collision.ReadyTask;}
            else{var scenery=Ui.Add(entry.Pane,new SceneryTools(session,document));entry.Scenery=scenery;entry.Flush=scenery.Flush;PreviewReady=scenery.ReadyTask;}return;
        }
        if(ClientTerrainCatalog.Accepts(key))
        {
            var terrain=Ui.Add(entry.Pane,new TerrainAuthoringView(session,document));entry.Terrain=terrain;entry.Flush=terrain.Flush;PreviewReady=terrain.ReadyTask;return;
        }
        if(key.Root==SourceRoot.ServerMaps&&key.RelativePath.EndsWith(".smd",StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var smd=SmdCodec.Read(document.Render());var preview=Ui.Add(entry.Pane,new WorldViewport(session,_=>{},true));entry.Preview=preview;
                var source=new SmdMapLayers(preview,session);var token=entry.Lifetime.Token;
                PreviewReady=BuildMap();entry.Flush=()=>{source.CommitGesture();if(source.Draft!=null)document.EditBytes(SmdCodec.Write(source.Draft.Document));};
                async Task BuildMap(){try{await preview.LoadMapAsync(SmdHeightAdapter.ToCanonical(smd),new(WorldTable.NpcPositions,Zone:32767),null,token,includeDatabase:false);token.ThrowIfCancellationRequested();source.Bind(smd,document.Snapshot);source.Changed+=()=>{if(Current())document.EditBytes(SmdCodec.Write(source.Draft.Document));};Ui.Add(entry.Pane,new SmdTools(session,source));}catch(OperationCanceledException){}catch(Exception e){if(Current())status.Text=e.Message;}}
            }
            catch(Exception e){Ui.Label(entry.Pane,"SMD is read-only: "+e.Message,12,Ui.Accent);}return;
        }
        if(key.Root==SourceRoot.ServerQuests&&key.RelativePath.EndsWith(".quest",StringComparison.OrdinalIgnoreCase))
        {
            try{var quest=Ui.Add(entry.Pane,new QuestSourceView(session,document.Snapshot,document.Render()));quest.SourceRequested+=k=>_ = SafeOpen(k);entry.Flush=()=>document.EditBytes(session.Sources.Blobs!.Read(quest.Capture().After!));}
            catch(Exception e){Ui.Label(entry.Pane,e.Message,12,Ui.Accent);}return;
        }
        var tabs=Ui.Add(entry.Pane,new TabContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});var structured=Ui.Column(tabs);structured.Name="Structured";structured.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var raw=Ui.Add(tabs,new CodeEdit{Name="Raw source",Editable=writable,GuttersDrawLineNumbers=true,SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});raw.AddThemeColorOverride("font_color",Ui.Text);
        bool rawDirty=false;try{raw.Text=document.Text;}catch(Exception e){raw.Text="Unsupported text encoding: "+e.Message;raw.Editable=false;}
        raw.TextChanged+=()=>rawDirty=true;
        SourceRowEditor? editor=null;
        entry.Flush=()=>{if(writable&&raw.Editable&&rawDirty){document.SetText(raw.Text);rawDirty=false;}else editor?.Flush();};
        try
        {
            if(!writable){Ui.Label(structured,"No supported codec authorises this file. The raw source is read-only.",12,Ui.Accent);tabs.CurrentTab=1;return;}
            var json=document.Json();var format=ServerJsonCatalog.Format(key.RelativePath);
            if(format==null){Ui.Label(structured,"Quest manifest · edit the ordered path array in Raw source or use explicit quest membership actions.",12,Ui.Muted);tabs.CurrentTab=1;return;}
            var bar=Ui.Add(structured,new HFlowContainer());var rowChoice=Ui.Add(bar,new OptionButton{CustomMinimumSize=new(180,28),FitToLongestItem=false});int selectedRow=entry.Ordinal,pageStart=0;var fieldsHost=Ui.Add(structured,new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled});var fields=Ui.Column(fieldsHost);fields.SizeFlagsHorizontal=SizeFlags.ExpandFill;
            void ShowRow(int index)
            {Ui.Clear(fields);selectedRow=index;entry.Ordinal=index;if(selected==entry)SelectedRowOrdinal=index;if(index<0||index>=json.RowCount)return;editor=Ui.Add(fields,new SourceRowEditor(session,document,json,json.RowAt(index)));}
            entry.RowCount=json.RowCount;entry.ChooseRow=ordinal=>{if(ordinal<0||ordinal>=json.RowCount)return false;selectedRow=ordinal;entry.Ordinal=ordinal;Choices();return true;};
            void Choices()
            {
                rowChoice.Clear();selectedRow=Math.Clamp(selectedRow,0,Math.Max(0,json.RowCount-1));pageStart=selectedRow/100*100;
                for(int i=pageStart;i<Math.Min(pageStart+100,json.RowCount);i++){var row=json.RowAt(i);rowChoice.AddItem(row.Token.JsonPointer+" · "+string.Join(" / ",format.KeyFields.Select(f=>row.Values[f]?.ToString()??"generated/default"))+" · "+(row.Values["Name"]?.ToString()??row.Values["MapName"]?.ToString()??format.Id));}
                if(json.RowCount>0){rowChoice.Select(selectedRow-pageStart);ShowRow(selectedRow);}else Ui.Label(fields,"No rows. Add a source row explicitly.",12,Ui.Muted);
            }
            void Commit(){document.EditBytes(json.Render());raw.Text=document.Text;rawDirty=false;Choices();}
            Ui.Button(bar,"Rows ‹",()=>{selectedRow=Math.Max(0,pageStart-100);Choices();});Ui.Button(bar,"Rows ›",()=>{if(pageStart+100<json.RowCount){selectedRow=pageStart+100;Choices();}});
            Ui.Button(bar,"Add row",()=>{var schema=format.Table is WorldTable table?session.Sources.WorldCatalog?.Tables.Single(x=>x.Table==table):null;json.Insert(schema==null?new JsonObject():(JsonObject)schema.Defaults.DeepClone(),json.RowCount);selectedRow=json.RowCount-1;Commit();});
            Ui.Button(bar,"Duplicate",()=>{if(json.RowCount>0){json.Insert(json.RowAt(selectedRow).Values,selectedRow+1);selectedRow++;Commit();}});
            Ui.Button(bar,"Remove",()=>{if(json.RowCount>0){json.Remove(json.RowAt(selectedRow).Token);Commit();}});
            Ui.Button(bar,"↑",()=>{if(selectedRow>0){json.Move(json.RowAt(selectedRow).Token,selectedRow-1);selectedRow--;Commit();}});Ui.Button(bar,"↓",()=>{if(selectedRow+1<json.RowCount){json.Move(json.RowAt(selectedRow).Token,selectedRow+1);selectedRow++;Commit();}});
            rowChoice.ItemSelected+=i=>{session.Shell.RememberContext();entry.Ordinal=pageStart+(int)i;ShowRow(entry.Ordinal);SelectedRowOrdinal=entry.Ordinal;session.Shell.RecordLocation();};Ui.Label(structured,SeedShardResolver.StructureWarning(format.Table??WorldTable.Warps),11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
            Choices();tabs.TabChanged+=i=>{if(i==1&&!rawDirty){raw.Text=document.Text;rawDirty=false;}if(i==0&&rawDirty){entry.Flush();Build(entry);}};
        }
        catch(Exception e){Ui.Label(structured,"Structured view unavailable: "+e.Message+" · invalid drafts remain editable in Raw source.",12,Ui.Accent).AutowrapMode=TextServer.AutowrapMode.WordSmart;tabs.CurrentTab=1;}
    }
    async Task Review(){try{await SourceChangeReview.Show(session,null,session.Sources.CaptureDrafts());RefreshTitles();}catch(Exception e){status.Text=e.Message;}}
    async Task SetMembership(bool include)
    {
        try{if(selected==null)return;var quest=selected.Document.Snapshot.Key;if(quest.Root!=SourceRoot.ServerQuests||!quest.RelativePath.EndsWith(".quest",StringComparison.OrdinalIgnoreCase))return;
        var manifest=Key(SourceRoot.ServerQuests,"quest-manifest.json");if(!entries.ContainsKey(manifest)){var s=session.Sources.Store!.Read(manifest);if(s.Exists)await OpenAsync(manifest);else Add(new(s,"[]"u8.ToArray()));}
        var entry=entries[manifest];entry.Flush();var json=entry.Document.Json();QuestManifest.Prepare(json,quest.RelativePath,include);entry.Document.EditBytes(json.Render());Build(entry);Select(entry);}catch(Exception e){status.Text=e.Message;}
    }
    async Task DeleteSelected(){if(selected==null)return;var entry=selected;await SetMembership(false);entry.Document.Delete();Build(entry);Select(entry);}
    void NewSource()
    {
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="Create source draft",DialogHideOnOk=false,OkButtonText="Create draft",MinSize=new(620,230)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(580,0);var path=Ui.Field(body,"Relative filename","Npcs.json / folder/npc.quest");var error=Ui.Label(body,"",11,Ui.Accent);
        d.Confirmed+=()=>{try{var key=Key((SourceRoot)roots.GetSelectedId(),path.Text);SourcePaths.Resolve(session.Sources.Profile!,key,true);if(!Authorized(key)||key.Root==SourceRoot.ServerMaps||ClientTerrainCatalog.Accepts(key)||VisualAsset(key))throw new InvalidOperationException("Create supported JSON/quest sources here; import a verified complete SMD for new maps.");var s=session.Sources.Store!.Read(key);if(s.Exists||entries.ContainsKey(key))throw new InvalidOperationException("Source already exists or is open.");Add(new(s,key.RelativePath.EndsWith("collision.bin",StringComparison.Ordinal)?CollisionCodec.Write([]):NpcVisualCatalog.Accepts(key)?Encoding.UTF8.GetBytes(NpcVisualCatalog.Default(key).ToJsonString()):key.RelativePath.EndsWith(".quest",StringComparison.OrdinalIgnoreCase)?Encoding.UTF8.GetBytes("Bind Npc 0\nOn greeting\n    Say \"Welcome\"\n    Topic \"Close\" goto close\n"):"[]"u8.ToArray()));d.Hide();d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
    void ConvertEncoding(Entry entry)
    {
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="Prepare explicit UTF-8 conversion",DialogHideOnOk=false,OkButtonText="Convert into draft",MinSize=new(650,300)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(610,190);
        Ui.Label(body,"Choose the actual source encoding. The resulting UTF-8 candidate uses normal file review and immutable backup history.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var choice=Ui.Add(body,new OptionButton());foreach(var (id,label) in new[]{(65001,"UTF-8"),(1200,"UTF-16 little-endian"),(1201,"UTF-16 big-endian"),(12000,"UTF-32 little-endian"),(12001,"UTF-32 big-endian"),(1252,"Windows Western European"),(1254,"Windows Turkish")})choice.AddItem(label,id);
        var error=Ui.Label(body,"",11,Ui.Accent);d.Confirmed+=()=>{try{entry.Document.EditBytes(TextConversion.ToUtf8(entry.Document.Render(),choice.GetSelectedId()));Build(entry);d.Hide();d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
    void ImportSmd()
    {
        var picker=Ui.Add(session.Shell,new FileDialog{Title="Import a complete SMD into a source draft",Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,Filters=["*.smd ; Server map"]});
        picker.FileSelected+=path=>
        {
            try
            {
                SourcePaths.AssertNoLinks(path);byte[] bytes;using(var stream=File.OpenRead(path)){bytes=new byte[SourceLimits.CheckedLength(stream.Length)];stream.ReadExactly(bytes);}
                var document=SmdCodec.Read(bytes);if(!document.Complete||SmdValidation.Validate(document).Count>0)throw new InvalidDataException("Import requires a complete supported SMD with valid sections.");
                var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="SMD source destination",DialogHideOnOk=false,OkButtonText="Prepare source draft",MinSize=new(620,250)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(580,160);var name=Ui.Field(body,"Relative map filename","new-map.smd",Path.GetFileName(path));var error=Ui.Label(body,"",11,Ui.Accent);
                d.Confirmed+=()=>{try{var key=Key(SourceRoot.ServerMaps,name.Text);SourcePaths.Resolve(session.Sources.Profile!,key,true);if(!key.RelativePath.EndsWith(".smd",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use a .smd destination.");OpenCandidate(new(session.Sources.Store!.Read(key),session.Sources.Blobs!.Put(bytes)));d.Hide();d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();picker.Hide();picker.QueueFree();d.PopupCentered();
            }
            catch(Exception e){status.Text=e.Message;picker.QueueFree();}
        };picker.Canceled+=()=>picker.QueueFree();picker.PopupCenteredRatio(.75f);
    }
    void ImportAsset()
    {
        var picker=Ui.Add(session.Shell,new FileDialog{Title="Import compatible GLB / glTF / RGBA8 PNG",Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,Filters=["*.glb,*.gltf ; Compatible glTF2 model","*.png ; RGBA8 PNG texture"]});
        picker.FileSelected+=path=>
        {
            picker.QueueFree();var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="Prepare compatible asset import",DialogHideOnOk=false,OkButtonText="Inspect dependencies",MinSize=new(780,510)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(740,400);
            Ui.Label(body,"Models use the verified core glTF2 subset; textures use RGBA8 PNG. Remote/escaping/linked dependencies and unsupported rigs are refused. Existing files remain unchanged until Review → Apply.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
            var relative=Ui.Field(body,"Client destination","assets/npcs/new_model.glb","assets/npcs/"+Path.GetFileNameWithoutExtension(path)+(Path.GetExtension(path).Equals(".png",StringComparison.OrdinalIgnoreCase)?".png":".glb"));var report=Ui.Add(body,new TextEdit{Editable=false,CustomMinimumSize=new(720,220)});FileChange? prepared=null;string? preparedPath=null;
            relative.TextChanged+=_=>{prepared=null;d.OkButtonText="Inspect dependencies";};d.Confirmed+=()=>{try
            {
                if(prepared==null){var key=Key(SourceRoot.ClientProject,relative.Text);if(!VisualAsset(key))throw new InvalidDataException("Select a compatible asset path under assets/npcs, assets/objects or assets/fx/tex.");prepared=AssetImportPlan.Prepare(session.Sources.Profile!,path,key,session.Sources.Store!);preparedPath=relative.Text;report.Text=string.Join("\n",prepared.Files.Select(f=>(f.Before.Exists?"Replace ":"Create ")+f.Before.Key.RelativePath+" · "+f.After!.Length+" bytes · "+f.After.Sha256[..16]))+"\nExisting source dependents:\n"+string.Join("\n",prepared.Files.SelectMany(f=>AssetDependencyAudit.Dependents(session.Sources.Store!,f.Before.Key)).Distinct().Select(k=>k.RelativePath));d.OkButtonText="Add reviewed import to drafts";return;}
                if(preparedPath!=relative.Text)throw new InvalidDataException("Destination changed; inspect again.");foreach(var file in prepared.Files)OpenCandidate(file);d.QueueFree();
            }catch(Exception e){report.Text=e.Message;prepared=null;d.OkButtonText="Inspect dependencies";}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
        };picker.Canceled+=()=>picker.QueueFree();picker.PopupCenteredRatio(.7f);
    }
    public void Undo(bool redo){if(selected==null)return;selected.Flush();if(selected.Terrain!=null){selected.Terrain.Undo(redo);return;}selected.Document.Undo(redo);Build(selected);}
    public void SetActive(bool active){Visible=active;foreach(var e in entries.Values){e.Preview?.SetActive(active&&e==selected);e.Terrain?.SetActive(active&&e==selected);e.Scenery?.SetActive(active&&e==selected);e.Collision?.SetActive(active&&e==selected);e.Visual?.SetActive(active&&e==selected);}}
    public override void _ExitTree(){foreach(var entry in entries.Values){entry.Lifetime.Cancel();entry.Lifetime.Dispose();entry.Registration?.Dispose();}}
    sealed class Entry(SourceDocument document,VBoxContainer pane)
    {public SourceDocument Document=document;public VBoxContainer Pane=pane;public Label? Badge;public IDisposable? Registration;public Action Flush=()=>{};public Func<int,bool> ChooseRow=_=>false;public int Ordinal,RowCount;public CancellationTokenSource Lifetime=new();public WorldViewport? Preview;public TerrainAuthoringView? Terrain;public SceneryTools? Scenery;public CollisionTools? Collision;public NpcVisualView? Visual;}
}
