using System.Text;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Quests(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;await session.Sources.EnsureRegisteredAsync();var store=session.Sources.Store!;var profile=store.Profile;
        SourceFileKey Key(string name)=>new(profile.Id,SourceRoot.ServerQuests,SourceIdentity.Root(profile.Roots[SourceRoot.ServerQuests]),name);
        const string valid="Bind Npc 880009\nOn greeting\n    Say \"Native quest fixture\"\n    Topic \"Close\" goto close\n";
        File.WriteAllBytes(SourcePaths.Resolve(profile,Key("quest-manifest.json"),true),"[\"native.quest\"]"u8.ToArray());
        File.WriteAllBytes(SourcePaths.Resolve(profile,Key("native.quest"),true),Encoding.UTF8.GetBytes(valid));var before=store.Read(Key("native.quest"));
        var d=Ui.Add(shell,new AcceptDialog{Title="NPC quest source",MinSize=new(950,720)});var view=Ui.Add(d,new QuestSourceView(session,before,store.Blobs.Read(before.Content!)));view.Position=new(18,42);view.CustomMinimumSize=new(900,610);d.PopupCentered();
        if((await view.ValidateAsync()).Count>0)throw new Exception("Valid native quest failed compilation.");
        view.Code.Text="Bind Npc 880009\nOn greeting\n    invalid-action ???";if((await view.ValidateAsync()).Count==0)throw new Exception("Invalid quest was accepted.");
        var rejected=await session.Sources.ApplyAsync(new(Guid.NewGuid(),[view.Capture()]));if(rejected.State!=FileApplyState.Invalid||store.Read(before.Key).Sha256!=before.Sha256)throw new Exception("Compiler errors did not block Apply.");
        view.Code.Text=valid.Replace("Native quest fixture","Staged quest fixture");
        var offline=new EditorSession(shell);
        try
        {
            await offline.InitializeAsync();
            var issues=await offline.Client.SendAsync<ValidationIssue[]>("sources.quest.validate",new{tickets=new[]{new SourceValidationTicket(before.Key,view.Capture().After!)}});
            if(issues.Length>0)throw new Exception("Disconnected helper cannot validate quest sources: "+string.Join("; ",issues.Select(x=>x.Field+": "+x.Message)));
            using var cancel=new CancellationTokenSource();
            var pending=offline.Client.SendAsync<ValidationIssue[]>("sources.quest.validate",new{tickets=new[]{new SourceValidationTicket(before.Key,view.Capture().After!)}},cancel.Token);cancel.Cancel();
            try{await pending;throw new Exception("Cancelled helper validation unexpectedly completed.");}catch(OperationCanceledException){}
            if(!offline.Client.IsClosed||!view.HasUnsavedChanges||store.Read(before.Key).Sha256!=before.Sha256)throw new Exception("Cancelled compiler helper wrote or lost the source draft.");
        }
        finally{offline.Dispose();}
        if(!view.HasUnsavedChanges||store.Read(before.Key).Sha256!=before.Sha256)throw new Exception("Validation changed its target or lost draft.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;d.PopupCentered();await EventGalleries.Capture(shell,"world-quests",size);}
        d.Hide();d.QueueFree();
    }
}
