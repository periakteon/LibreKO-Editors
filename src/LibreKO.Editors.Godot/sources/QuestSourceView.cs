using System.Text.RegularExpressions;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public partial class QuestSourceView:VBoxContainer
{
    readonly EditorSession session;readonly TextDocument original;readonly int generation;
    readonly ItemList diagnostics;readonly Label status=null!;readonly Label names;
    public SourceFileSnapshot Snapshot{get;}
    public CodeEdit Code{get;}
    public event Action<SourceFileKey>? SourceRequested;
    public bool HasUnsavedChanges=>Code.Text!=original.Text;
    public QuestSourceView(EditorSession session,SourceFileSnapshot snapshot,byte[] bytes)
    {
        this.session=session;Snapshot=snapshot;generation=session.Sources.Generation;original=TextDocument.Decode(bytes);SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        Ui.Heading(this,"QUEST SOURCE",14);Ui.Label(this,snapshot.Key.RelativePath+" · selected server source · compiler validation only",12,Ui.Muted);
        names=Ui.Label(this,"Resolving NPC names…",12,Ui.Accent);names.AutowrapMode=TextServer.AutowrapMode.WordSmart;names.MaxLinesVisible=2;
        var bar=Ui.Add(this,new HFlowContainer());var search=Ui.Input(bar,"Find in source");search.CustomMinimumSize=new(180,28);
        Ui.Button(bar,"Find",()=>Find(search.Text));Ui.Button(bar,"Validate",()=>_ = session.RunAsync(OperationKind.Source,async ct=>await ValidateAsync(ct:ct)));Ui.Button(bar,"Source folder",()=>OS.ShellOpen(Path.GetDirectoryName(SourcePaths.Resolve(session.Sources.Profile!,Snapshot.Key,false))!));
        Code=Ui.Add(this,new CodeEdit{Text=original.Text,GuttersDrawLineNumbers=true,LineFolding=true,GuttersDrawFoldGutter=true,IndentSize=4,IndentUseSpaces=true,SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new(200,280)});
        Code.AddThemeColorOverride("font_color",Ui.Text);var colors=new CodeHighlighter{NumberColor=new("a9c5ce"),SymbolColor=Ui.Muted};foreach(var keyword in new[]{"Bind","Npc","Zone","Quest","On","Say","Topic","If","Else","Rewards","Give","Requires","include"})colors.AddKeywordColor(keyword,Ui.Accent);Code.SyntaxHighlighter=colors;
        diagnostics=Ui.Add(this,new ItemList{CustomMinimumSize=new(200,105),TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis});diagnostics.ItemSelected+=i=>Navigate(diagnostics.GetItemText((int)i));
        var related=Ui.Add(this,new HFlowContainer());
        foreach(Match match in Regex.Matches(original.Text,"(?im)^\\s*include\\s+(?:\"([^\"]+)\"|(\\S+))"))
        {
            var path=match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value;if(!Path.HasExtension(path))path+=".quest";var relative=path;
            Ui.Button(related,"Include · "+relative,()=>{try{var directory=Path.GetDirectoryName(Snapshot.Key.RelativePath)?.Replace('\\','/');var k=Snapshot.Key with{RelativePath=string.IsNullOrEmpty(directory)?relative:directory+"/"+relative};SourcePaths.Resolve(session.Sources.Profile!,k,false);SourceRequested?.Invoke(k);}catch(Exception e){status.Text=e.Message;}});
        }
        status=Ui.Label(this,"Unapplied text draft · saving requires a valid include graph and reviewed manifest",11,Ui.Muted);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=2;
        Code.TextChanged+=()=>{diagnostics.Clear();status.Text="Text changed · validate the current draft before Apply";};
    }
    public override void _Ready()=>_ = ResolveNames();
    public FileMutation Capture()
    {Ensure();return new(Snapshot,session.Sources.Blobs!.Put(original.Encode(Code.Text)));}
    void Ensure(){if(generation!=session.Sources.Generation)throw new InvalidOperationException("Source profile changed; reopen the quest draft.");}
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(IReadOnlyList<SourceValidationTicket>? staged=null,CancellationToken ct=default)
    {
        try
        {
            Ensure();await session.Sources.EnsureRegisteredAsync(ct);var own=Capture();var tickets=(staged??[]).Where(x=>x.Key!=Snapshot.Key).Append(new(Snapshot.Key,own.After!)).ToArray();
            var issues=await session.Client.SendAsync<ValidationIssue[]>("sources.quest.validate",new{tickets},ct);Ensure();
            if(!IsInstanceValid(this)||!IsInsideTree())return issues;diagnostics.Clear();foreach(var issue in issues)diagnostics.AddItem(issue.Field+" · "+issue.Message);
            status.Text=issues.Length==0?"Compiler validation passed · no script was executed · draft is unapplied":issues.Length+" compiler/manifest errors block Apply";return issues;
        }
        catch(Exception e){if(session.Client.IsClosed)throw;if(IsInstanceValid(this)&&IsInsideTree())status.Text=e.Message;return [new(Snapshot.Key.RelativePath,e.Message)];}
    }
    async Task ResolveNames()
    {
        try
        {
            await session.Sources.EnsureRegisteredAsync();
            var keys=Regex.Matches(Code.Text,@"(?im)\bBind\s+Npc\s+(\d+)").Select(m=>new ReferenceIdentity("npcs",int.Parse(m.Groups[1].Value),IsMonster:false)).Distinct().Take(100).ToArray();
            var found=await session.Client.SendAsync<NamedReference[]>("references.named.resolve",new NamedReferenceResolve(keys,session.Sources.Profile?.Id));
            if(generation==session.Sources.Generation&&IsInstanceValid(this)&&IsInsideTree())names.Text=found.Length==0?"No NPC binding in this source":string.Join(" · ",found.Select(x=>x.Key.Id+" · "+(x.Label.Length>0?x.Label:x.State)+" · "+x.Source));
        }
        catch(Exception e){if(IsInstanceValid(this)&&IsInsideTree())names.Text="Names unavailable · "+e.Message;}
    }
    void Find(string text)
    {
        if(text.Length==0)return;int at=Code.Text.IndexOf(text,StringComparison.OrdinalIgnoreCase);if(at<0)return;var before=Code.Text[..at].Split('\n');Code.SetCaretLine(before.Length-1);Code.SetCaretColumn(before[^1].Length);Code.GrabFocus();
    }
    void Navigate(string text)
    {var parts=text.Split('·')[0].Trim().Split(':');if(parts.Length<3)return;if(parts[0]!=Snapshot.Key.RelativePath){SourceRequested?.Invoke(Snapshot.Key with{RelativePath=parts[0]});return;}if(int.TryParse(parts[1],out var line)&&int.TryParse(parts[2],out var col)){Code.SetCaretLine(Math.Max(0,line-1));Code.SetCaretColumn(Math.Max(0,col-1));Code.GrabFocus();}}
}
