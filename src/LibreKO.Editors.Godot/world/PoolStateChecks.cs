using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class PoolStateChecks
{
    public static async Task Run(EditorShell shell,string mode)
    {
        _=SourceChecks.Marker(shell);await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;
        await app.SelectAsync(new(WorldTable.Npcs,880019,IsMonster:true));await app.Contents.ReadyTask;app.ShowContents();var board=app.Contents;
        if(mode=="pool-stale")
        {
            var pending=board.OpenPoolAsync(881400);board.ChooseMode(1);await pending;
            if(board.Mode!=1)throw new Exception("A stale pool response replaced the newer Merchant shop view.");
        }
        else if(mode=="pool-replay")
        {
            await board.OpenPoolAsync(881400);board.ChooseMode(0);board.SelectSlot(5);var captured=board.CaptureState();
            board.ChooseMode(1);await board.RestoreStateAsync(captured);
            if(!JsonNode.DeepEquals(captured,board.CaptureState()))throw new Exception("Restoring a board with a previously opened pool lost its mode, page or slot.");
        }
        else
        {
            await app.SelectAsync(new(WorldTable.MakeItemGroups,881400));await board.ReadyTask;app.ShowContents();
            board.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Shared users…").EmitSignal(BaseButton.SignalName.Pressed);
            await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
            var dialog=shell.GetChildren().OfType<AcceptDialog>().SingleOrDefault(d=>d.IsVisible());
            if(dialog==null)throw new Exception("Direct pool selection has no Shared users view.");
            var list=dialog.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();var deadline=DateTime.UtcNow.AddSeconds(10);
            while(list.ItemCount==0&&DateTime.UtcNow<deadline)await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
            var labels=Enumerable.Range(0,list.ItemCount).Select(list.GetItemText).ToArray();
            if(!labels.Any(l=>l.Contains("8819")&&l.Contains("Monster"))||!labels.Any(l=>l.Contains("8819")&&l.Contains("NPC")))throw new Exception("Pool Shared users omitted typed consumer groups.");
            dialog.QueueFree();
        }
    }
}
