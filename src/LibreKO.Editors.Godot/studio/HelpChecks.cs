using Godot;
using System.Text.Json;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;

public static class HelpChecks
{
    public static async Task Run(EditorShell shell)
    {
        var profile=JsonSerializer.Serialize(shell.Session.Profile);var active=shell.Active;
        var button=shell.FindChildren("*","Button",true,false).OfType<Button>().SingleOrDefault(b=>b.Text=="Help");
        if(button==null)throw new Exception("Global Help entry is missing.");
        shell._Input(new InputEventKey{Keycode=Key.F1,Pressed=true});await Frame();
        var dialog=shell.GetChildren().OfType<AcceptDialog>().SingleOrDefault(d=>d.Name=="EditorHelp"&&d.Visible);
        if(dialog==null)throw new Exception("F1 did not open Help.");
        var search=dialog.FindChildren("HelpSearch","LineEdit",true,false).OfType<LineEdit>().Single();
        var topics=dialog.FindChildren("HelpTopics","ItemList",true,false).OfType<ItemList>().Single();
        var article=dialog.FindChildren("HelpArticle","RichTextLabel",true,false).OfType<RichTextLabel>().Single();
        if(topics.ItemCount!=10)throw new Exception("Help topics did not load offline.");
        search.Text="client verified";search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);await Frame();
        var client=Enumerable.Range(0,topics.ItemCount).FirstOrDefault(i=>topics.GetItemText(i).StartsWith("Client release"),-1);
        if(client<0)throw new Exception("Help search lost the production client guide.");
        topics.Select(client);topics.EmitSignal(ItemList.SignalName.ItemSelected,(long)client);
        if(!article.Text.Contains("Verified client output")||!article.Text.Contains("content/terrain.pck"))throw new Exception("Client production steps are incomplete.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,760)})
        {
            shell.GetWindow().Size=size;await Frame();dialog.PopupCentered(new(Math.Min(1160,size.X-80),size.Y-80));await Frame();
            if(dialog.Size.X>size.X||dialog.Size.Y>size.Y)throw new Exception("Help window exceeds supported bounds.");
            if(article.Size.X<500||article.Size.X<topics.Size.X)throw new Exception("Help article is squeezed by the topic sidebar.");
            if(DisplayServer.GetName()!="headless")
            {Directory.CreateDirectory(Path.Combine(shell.Session.Root,"artifacts"));shell.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(shell.Session.Root,"artifacts",size.X==1280?"help-client-compact.png":"help-client.png"));}
        }
        search.Text="";search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
        var database=Enumerable.Range(0,topics.ItemCount).Single(i=>topics.GetItemText(i)=="Database configuration");topics.Select(database);topics.EmitSignal(ItemList.SignalName.ItemSelected,(long)database);
        if(!article.Text.Contains("row count")||!article.Text.Contains("+reloaddrops"))throw new Exception("Database seeding/activation guidance is incomplete.");
        dialog.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Current targets…").EmitSignal(BaseButton.SignalName.Pressed);await Frame();
        var targets=dialog.GetChildren().OfType<AcceptDialog>().Single(d=>d.Visible);
        targets.EmitSignal(AcceptDialog.SignalName.Canceled);await Frame();
        dialog.EmitSignal(AcceptDialog.SignalName.Canceled);await Frame();
        if(shell.GetChildren().OfType<AcceptDialog>().Any(d=>d.Name=="EditorHelp"&&d.Visible))throw new Exception("Help did not close independently.");
        ClientBuildView.Show(shell.Session);await Frame();var build=shell.GetChildren().OfType<AcceptDialog>().Single(d=>d.Title=="Build authored client");
        build.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Production steps…").EmitSignal(BaseButton.SignalName.Pressed);await Frame();
        dialog=shell.FindChildren("EditorHelp","AcceptDialog",true,false).OfType<AcceptDialog>().Single(d=>d.Visible);
        if(dialog.GetParent()!=build)throw new Exception("Contextual Help must be owned by its modal build window.");
        if(!dialog.FindChildren("HelpArticle","RichTextLabel",true,false).OfType<RichTextLabel>().Single().Text.Contains("Verified client output"))throw new Exception("Build help did not choose client instructions.");
        dialog.EmitSignal(AcceptDialog.SignalName.Canceled);build.EmitSignal(AcceptDialog.SignalName.Canceled);await Frame();
        var source=new SourceFileKey("help-check",SourceRoot.ServerData,"help-check","ItemSells.json");
        var reviewTask=SourceChangeReview.Show(shell.Session,null,new FileChange(Guid.NewGuid(),[new(new(source,false,"",null),null)]));await Frame();
        var review=shell.GetChildren().OfType<AcceptDialog>().Single(d=>d.Name=="SourceChangeReview");
        review.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Production steps…").EmitSignal(BaseButton.SignalName.Pressed);await Frame();
        dialog=review.GetChildren().OfType<AcceptDialog>().Single(d=>d.Name=="EditorHelp"&&d.Visible);
        if(!dialog.FindChildren("HelpArticle","RichTextLabel",true,false).OfType<RichTextLabel>().Single().Text.Contains("Deploy merchant shops"))throw new Exception("Source review help did not choose direct-load shop instructions.");
        dialog.EmitSignal(AcceptDialog.SignalName.Canceled);review.EmitSignal(AcceptDialog.SignalName.Canceled);await reviewTask;await Frame();
        if(shell.Active!=active||profile!=JsonSerializer.Serialize(shell.Session.Profile))throw new Exception("Help changed the workspace or profile.");
        GD.Print("Offline Help/F1, search, production guides, compact layout, contextual build link and close verified");
        async Task Frame()=>await shell.ToSignal(shell.GetTree().CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
    }
}
