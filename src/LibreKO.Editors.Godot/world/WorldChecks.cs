using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    public static async Task Run(EditorShell shell, string mode)
    {
        if (mode == "world-smd") { await Smd(shell); return; }
        if (mode == "world-quests") { await Quests(shell); return; }
        if(mode=="world-terrain"){await Terrain(shell);return;}
        if(mode=="world-scenery"){await Scenery(shell);return;}
        if(mode=="world-npc-visuals"){await NpcVisuals(shell);return;}
        if(mode is "world-json" or "world-seed-compare" or "world-file-apply" or "world-source-cancel"){await Sources(shell,mode);return;}
        if (mode == "world-spatial") { await Spatial(shell); return; }
        if (mode == "world-gallery") { await WorldGalleries.Run(shell); return; }
        if (mode is "world-editor" or "world-drafts") { await Workspace(shell, mode); return; }
        _ = SourceChecks.Marker(shell);
        var session = shell.Session;
        if (!session.Connected || !session.ConnectionString.Contains("Database=libreko_editor_test_", StringComparison.OrdinalIgnoreCase) || !session.ConnectionString.Contains("33307")) throw new Exception("World checks require the marked native fixture and its isolated database.");
        async Task Wait(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!ready()) { if (DateTime.UtcNow > deadline) throw new Exception("World UI check timed out."); await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame); }
        }
        var catalog = await session.Client.SendAsync<WorldCatalog>("world.catalog", new { });
        var objectId = await session.Client.SendAsync<int>("world.next-id", new { table = WorldTable.MapObjects });
        // Existing event fixtures intentionally contain unresolved low-numbered prototypes.
        var npcId = Random.Shared.Next(800_000_000, 900_000_000);
        if ((await session.Client.SendAsync<JsonElement>("world.get", new WorldKey(WorldTable.Npcs, npcId, IsMonster: true))).TryGetProperty("key", out _)) throw new Exception("Native world fixture ID collision.");
        var obj = (JsonObject)catalog.Tables.Single(t => t.Table == WorldTable.MapObjects).Defaults.DeepClone(); obj["Id"] = objectId; obj["ZoneId"] = 210;
        var npc = (JsonObject)catalog.Tables.Single(t => t.Table == WorldTable.Npcs).Defaults.DeepClone(); npc["Id"] = npcId; npc["IsMonster"] = true; npc["Name"] = "Native world history fixture";
        var change = new WorldChange(Guid.NewGuid(), [new(new(WorldTable.MapObjects, objectId), null, obj), new(new(WorldTable.Npcs, npcId, IsMonster: true), null, npc)], []);
        var saved = await session.Client.SendAsync<WorldSaveResult>("world.save", change);
        if (saved.Status != SaveStatus.Saved) throw new Exception(saved.Message + string.Join(";", saved.Issues ?? []));
        session.NotifyDatabaseChange();
        await shell.RequestWorkspaceAsync("events"); var events = (EventsWorkspace)shell.Active!;
        await events.SelectFamilyAsync(EventFamily.MapObject); await events.SelectAsync(new(EventFamily.MapObject, objectId));
        await EventHistoryView.OpenAsync(session);
        var eventDialog = shell.FindChild("EventHistory", true, false)!;
        var related = eventDialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Related World operations…"); related.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(() => shell.GetChildren().OfType<AcceptDialog>().Any(d => d.Title == "Related database operations"));
        var chooser = shell.GetChildren().OfType<AcceptDialog>().Single(d => d.Title == "Related database operations");
        var visibleList = chooser.FindChildren("*", "ItemList", true, false).OfType<ItemList>().Single();
        if (!Enumerable.Range(0, visibleList.ItemCount).Any(i => visibleList.GetItemText(i).Contains("World · "))) throw new Exception("Events related history hides World-origin records.");
        chooser.QueueFree(); await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        var router = new HistoryRouter(session); var link = (await router.ListAsync("events")).Single(x => x.OperationId == change.OperationId);
        if (link.Origin != "World" || link.Targets.Count != 2) throw new Exception("World batch was reclassified or split into event changes.");
        bool rejected = false; try { await router.OpenAsync(link with { ScopeId = "different-database" }); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("History router crossed database scopes.");
        await router.OpenAsync(link);
        var dialog = shell.FindChild("WorldHistory", true, false)!;
        var reverse = dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Reverse operation");
        await Wait(() => !reverse.Disabled); reverse.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(() => session.Operations.CanMutate && reverse.Disabled);
        foreach (var row in change.Rows)
        {
            var live = await session.Client.SendAsync<JsonElement>("world.get", row.Key);
            if (live.TryGetProperty("key", out _)) throw new Exception("World history reversal did not apply the complete original batch: " + ((Label)dialog.FindChild("WorldHistoryStatus", true, false)).Text);
        }
        dialog.QueueFree();
    }
}
