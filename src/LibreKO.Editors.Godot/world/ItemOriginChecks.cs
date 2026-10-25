using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class ItemOriginChecks
{
    public static async Task Run(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);
        await shell.RequestWorkspaceAsync("content");var app=(EditorApp)shell.Active!;app.Module=0;
        await app.SelectRecord(123450001);await app.PropertiesReady;app.Inspector.CurrentTab=2;
        await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);await app.Origins!.ReadyTask;
        var links=app.Origins.FindChildren("*","Button",true,false).OfType<Button>().Where(b=>b.Text=="NPC / monster & spawns").ToArray();
        if(links.Length<4)throw new Exception("Direct and pooled typed origins are missing from the item editor.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;await EventGalleries.Capture(shell,"item-origins",size);}
        links[0].EmitSignal(BaseButton.SignalName.Pressed);
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while(shell.Active is not WorldWorkspace world||world.Selected?.Table!=WorldTable.Npcs)
        {if(DateTime.UtcNow>deadline)throw new Exception("Origin prototype link did not navigate.");await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);}
        while(!shell.Session.Operations.CanMutate)await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        await shell.NavigateHistoryAsync(false);
        if(shell.Active!=app||app.SelectedId!=123450001||app.Inspector.CurrentTab!=2)throw new Exception("Back lost the item Origins tab.");
        GD.Print("Item origins and typed prototype links verified");
    }
}
