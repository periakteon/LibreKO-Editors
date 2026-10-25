using System.Text;
using System.Text.RegularExpressions;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Help;

namespace LibreKO.Editors.Desktop;

public static class HelpView
{
    public static void Show(EditorSession session,string? topic=null,IReadOnlyList<string>? destinations=null)
    {
        var existing=session.Shell.FindChildren("EditorHelp","AcceptDialog",true,false).OfType<AcceptDialog>().FirstOrDefault(d=>!d.IsQueuedForDeletion());
        if(existing!=null){existing.PopupCentered();return;}
        topic??=ContextTopic(session);
        Node owner=session.Shell.FindChildren("*","AcceptDialog",true,false).OfType<AcceptDialog>().LastOrDefault(d=>d.Visible&&!d.IsQueuedForDeletion()) as Node??session.Shell;
        var dialog=Ui.Add(owner,new AcceptDialog{Name="EditorHelp",Title="LibreKO Help · editing & production",MinSize=new(1020,610),Unresizable=false});
        dialog.GetOkButton().Text="Close";
        var body=Ui.Column(dialog);body.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        body.OffsetLeft=16;body.OffsetTop=16;body.OffsetRight=-16;body.OffsetBottom=-58;
        var header=Ui.Row(body);Ui.Heading(header,"HELP & PRODUCTION GUIDES",18).AddThemeColorOverride("font_color",Ui.Accent);
        Ui.Add(header,new Control{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});
        Ui.Button(header,"Current targets…",()=>ShowTargets(dialog,ContextText(session,destinations)));
        var orientation=Ui.Label(body,(session.Connected?"Database: "+session.DatabaseName:"Database: disconnected")+" · Source profile: "+(session.Sources.Profile?.Label??"not selected")+"\nThese selections are not proof of production identity. Check the actual runtime destination before deployment.",12,Ui.Muted);
        orientation.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var search=Ui.Input(body,"Search help: client update, shop, row count, rollback…");search.Name="HelpSearch";
        var split=Ui.Row(body);split.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        var library=Ui.Column(split);library.CustomMinimumSize=new(275,0);library.SizeFlagsHorizontal=Control.SizeFlags.Fill;Ui.Label(library,"GUIDES",11,Ui.Accent);
        var list=Ui.Add(library,new ItemList{Name="HelpTopics",SizeFlagsVertical=Control.SizeFlags.ExpandFill,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis});
        var count=Ui.Label(library,"",11,Ui.Muted);
        var frame=Ui.Add(split,new PanelContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});frame.AddThemeStyleboxOverride("panel",Ui.Surface(14,true));
        var column=Ui.Column(frame);var summary=Ui.Label(column,"",12,Ui.Muted);summary.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var article=Ui.Add(column,new RichTextLabel{Name="HelpArticle",BbcodeEnabled=true,SelectionEnabled=true,ScrollActive=true,SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});
        article.AddThemeFontSizeOverride("normal_font_size",14);article.AddThemeConstantOverride("line_separation",5);article.AddThemeColorOverride("default_color",Ui.Text);
        var footer=Ui.Row(body);var copy=Ui.Button(footer,"Copy guide",()=>{});
        Ui.Label(footer,"Read-only help · no commands are executed · F1 opens Help",11,Ui.Muted);
        HelpGuide? selected=null;IReadOnlyList<HelpGuide> shown=[];
        void Select(int index)
        {
            selected=index>=0&&index<shown.Count?shown[index]:null;copy.Disabled=selected==null;
            summary.Text=selected?.Summary??"No matching guide. Try fewer words.";
            article.Text=selected==null?"":Render(selected.Markdown);article.ScrollToLine(0);
        }
        void Filter(string query)
        {
            var preferred=selected?.Id??topic;shown=HelpCatalog.Search(query);list.Clear();
            foreach(var guide in shown){var index=list.AddItem(guide.Title);list.SetItemTooltip(index,guide.Summary);}
            count.Text=$"{shown.Count} / {HelpCatalog.All.Count} guides";
            var indexToSelect=Enumerable.Range(0,shown.Count).FirstOrDefault(i=>shown[i].Id==preferred,shown.Count>0?0:-1);
            if(indexToSelect>=0)list.Select(indexToSelect);Select(indexToSelect);
        }
        list.ItemSelected+=i=>Select((int)i);search.TextChanged+=Filter;
        copy.Pressed+=()=>{if(selected!=null)DisplayServer.ClipboardSet(selected.Markdown+"\n\n"+ContextText(session,destinations));};
        void Close(){dialog.Hide();dialog.QueueFree();}
        dialog.Confirmed+=Close;dialog.Canceled+=Close;dialog.CloseRequested+=Close;
        Filter("");var size=session.Shell.GetWindow().Size;dialog.PopupCentered(new(Math.Min(1160,size.X-80),Math.Min(790,size.Y-80)));
    }
    static string ContextTopic(EditorSession session)
    {
        if(session.Shell.Active is WorldWorkspace world&&world.SourceView?.IsVisibleInTree()==true&&world.SourceView.SelectedDocument is {} source)
            return HelpCatalog.ForSource(source.Snapshot.Key.Root,source.Snapshot.Key.RelativePath);
        return session.Shell.Active switch{EditorApp content=>content.Module==0?"database":"players",WorldWorkspace=>"database",EventsWorkspace=>"database",_=>"overview"};
    }
    static string ContextText(EditorSession session,IReadOnlyList<string>? destinations)
    {
        var text=new StringBuilder("CURRENT EDITOR SELECTIONS (not verified production targets)\n");
        text.AppendLine("Database: "+(session.Connected?session.DatabaseName:"disconnected"));
        text.AppendLine("Editor: "+(session.Shell.Active?.Id??"Home"));
        text.AppendLine("Installed reference client: "+session.Profile.AssetRoot);
        if(session.Sources.Profile is {} profile)
        {text.AppendLine("Source profile: "+profile.Label);foreach(var root in profile.Roots)text.AppendLine(root.Key+": "+root.Value);}
        else text.AppendLine("Source profile: not configured");
        if(destinations is {Count:>0}){text.AppendLine("Reviewed destinations:");foreach(var path in destinations)text.AppendLine(path);}
        else if(session.Shell.Active is {} active){foreach(var destination in active.PendingDestinations)text.AppendLine("Pending: "+destination);}
        return text.ToString();
    }
    static void ShowTargets(Node owner,string text)
    {
        var dialog=Ui.Add(owner,new AcceptDialog{Title="Selected targets · verify before production",MinSize=new(900,520)});
        var view=Ui.Add(dialog,new TextEdit{Editable=false,Text=text,Position=new(16,20),CustomMinimumSize=new(860,410),WrapMode=TextEdit.LineWrappingMode.Boundary});
        dialog.Confirmed+=()=>dialog.QueueFree();dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
    static string Render(string markdown)
    {
        var result=new StringBuilder();bool code=false;
        foreach(var raw in markdown.Replace("\r","").Split('\n'))
        {
            if(raw.StartsWith("```")){result.AppendLine(code?"[/code]":"[code]");code=!code;continue;}
            var line=raw.Replace("[","[lb]");
            if(!code&&line.StartsWith("# "))line="[font_size=23][color=#dbc18c][b]"+line[2..]+"[/b][/color][/font_size]";
            else if(!code&&line.StartsWith("## "))line="[font_size=18][color=#c7a55c][b]"+line[3..]+"[/b][/color][/font_size]";
            else if(!code)line=Regex.Replace(line,@"\*\*(.+?)\*\*","[b]$1[/b]");
            result.AppendLine(line);
        }
        if(code)result.Append("[/code]");return result.ToString();
    }
}
