using System.Globalization;
using System.Text.Json;
using Godot;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Workspace(EditorShell shell, string mode)
    {
        _ = SourceChecks.Marker(shell); var session = shell.Session;
        if (!session.ConnectionString.Contains("33307") || !session.ConnectionString.Contains("libreko_editor_test_native")) throw new Exception("World checks require the isolated native database.");
        async Task Wait(Func<bool> ready)
        { var deadline = DateTime.UtcNow.AddSeconds(20); while (!ready()) { if (DateTime.UtcNow > deadline) throw new Exception("World workspace operation timed out."); await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame); } }
        var helper = session.Client;
        var open = shell.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Open Map & NPC Editor"); open.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(() => shell.Active?.Id == "world" && session.Operations.CanMutate);
        if (shell.Active?.Id != "world") throw new Exception("Map & NPC did not open");
        var app = (WorldWorkspace)shell.Active;
        if (mode == "world-editor")
        {
            await app.SelectTableAsync(WorldTable.Npcs); await app.SearchAsync("880019");
            if (app.Documents.Count != 2 || !app.Library.GetItemText(0).Contains("fixture") || !app.Library.GetItemText(1).Contains("fixture")) throw new Exception("Typed NPC/monster name search failed.");
            (WorldKey Key, string Field)[] cases = [(new(WorldTable.ZoneInfos,210),"ServerNo"),(new(WorldTable.StartPositions,210),"KarusX"),(new(WorldTable.Homes,1),"KarusZoneX"),
                (new(WorldTable.Npcs,880019,IsMonster:true),"Hp"),(new(WorldTable.NpcPositions,885001),"RegTime"),(new(WorldTable.NpcItems,8819,IsMonster:true),"Percent1"),
                (new(WorldTable.MakeItemGroups,881400),"Items"),(new(WorldTable.MonsterSummons,885003),"Probability"),(new(WorldTable.ItemExchanges,885004),"OriginCount1"),
                (new(WorldTable.PetLevels,201),"MaxHp"),(new(WorldTable.PetTransforms,885008),"Size"),(new(WorldTable.GameEvents,7,212),"Cond1"),
                (new(WorldTable.MapObjects,885005),"X"),(new(WorldTable.EventTriggers,885006),"TriggerNum"),(new(WorldTable.Warps,885007),"Fee")];
            foreach (var (key, field) in cases)
            {
                await app.SelectAsync(key);
                var schema = app.Catalog!.Tables.Single(t => t.Table == key.Table);
                var rendered = app.Form.FindChildren("Field_*", "Control", true, false).OfType<Control>().Select(c => c.Name.ToString()[6..]).ToHashSet();
                if (!rendered.SetEquals(schema.Fields.Where(f => f.Field.Writable).Select(f => f.Field.Name))) throw new Exception("World form omits persisted fields: " + key.Table);
                var original = app.Draft!.Rows.Single(r => r.Key == key).Values[field]!.ToString();
                var edited = key.Table == WorldTable.MakeItemGroups ? original + ",123450001" : (decimal.Parse(original, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                void Set(string text) { var input = (LineEdit)app.Form.FindChild("Field_" + field, true, false); input.Text = text; input.EmitSignal(LineEdit.SignalName.TextSubmitted, text); }
                Set(edited); if (!await app.SaveAsync()) throw new Exception("World form save failed: " + key.Table);
                await app.SelectAsync(key); if (app.Draft!.Rows.Single(r => r.Key == key).Values[field]!.ToString() != edited) throw new Exception("World form did not persist " + key.Table);
                Set(original); if (!await app.SaveAsync()) throw new Exception("World fixture restoration failed: " + key.Table);
                if (key.Table == WorldTable.NpcItems && !app.Form.FindChildren("ItemIcon_*", "TextureRect", true, false).Any()) throw new Exception("Drop item visuals are missing.");
            }
            var target = new NavigationTarget("world", "Npcs", new() { ["id"] = 880019, ["isMonster"] = false }, session.HistoryRoot);
            await shell.OpenTargetAsync(target);
            if (app.Selected?.IsMonster != false) throw new Exception("Typed reference opened the wrong prototype.");
            await shell.RequestWorkspaceAsync("events"); await shell.RequestWorkspaceAsync("world");
            if (!ReferenceEquals(helper, session.Client)) throw new Exception("Workspace switching replaced the shared helper.");
            return;
        }
        var npcKey = new WorldKey(WorldTable.Npcs, 880019, IsMonster:true); await app.SelectAsync(npcKey);
        var initial = app.Draft!.Rows.Single().Values["Hp"]!.ToString();
        var hp = (LineEdit)app.Form.FindChild("Field_Hp",true,false); hp.GrabFocus(); var focusedValue = (int.Parse(initial)+2).ToString(); hp.Text = focusedValue;
        shell._Input(new InputEventKey { Pressed=true, CtrlPressed=true, Keycode=Key.S });
        await Wait(()=>session.Operations.CanMutate && !app.HasUnsavedChanges);
        if (app.Draft!.Rows.Single().Values["Hp"]!.ToString() != focusedValue) throw new Exception("Focused Ctrl+S lost the field edit.");
        hp = (LineEdit)app.Form.FindChild("Field_Hp",true,false); hp.Text="invalid"; hp.EmitSignal(LineEdit.SignalName.TextChanged,hp.Text);
        if (app.CanSave || await app.SaveAsync()) throw new Exception("Invalid world value was saved."); app.Discard();
        var count = app.Draft!.Rows.Count; await app.DuplicateAsync(); if (app.Draft.Rows.Count != count+1) throw new Exception("Duplicate missing from world draft."); app.Undo(false);
        if (app.Draft!.Rows.Count != count || app.HasUnsavedChanges) throw new Exception("Duplication was not one undo step.");
        var other = await session.Client.SendAsync<WorldRowSnapshot>("world.get", npcKey with { IsMonster=false }); await app.IncludeAsync(other);
        var before = JsonSerializer.Serialize(app.Draft!.Rows); var changes = app.Draft.Rows.Select(r => { var after = (System.Text.Json.Nodes.JsonObject)r.Values.DeepClone(); after["Hp"] = int.Parse(after["Hp"]!.ToString())+5; return new WorldRowChange(r.Key,r,after); }).ToArray();
        app.ApplyBatch(changes); app.Undo(false); if (before != JsonSerializer.Serialize(app.Draft!.Rows)) throw new Exception("World batch was not one undo step.");
        app.SetValue(npcKey,"Hp",System.Text.Json.Nodes.JsonValue.Create(int.Parse(initial)+3));
        var nav=shell.RequestWorkspaceAsync("events"); var guard=shell.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title=="Unsaved changes" && d.Visible); guard.EmitSignal(ConfirmationDialog.SignalName.Canceled); await nav;
        if (shell.Active!=app || !app.HasUnsavedChanges) throw new Exception("Cancel lost world edits.");
        var sourceId=session.Sources.Profile!.Id; SourceSettingsView.Show(session); var sources=shell.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title=="Source profiles");
        sources.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Disable file editing").EmitSignal(BaseButton.SignalName.Pressed);
        guard=shell.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title=="Unsaved changes" && d.Visible); guard.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame); if (session.Sources.Profile?.Id!=sourceId || !app.HasUnsavedChanges) throw new Exception("Source change ignored the world dirty guard."); sources.QueueFree();
        await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame); await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        nav=shell.RequestWorkspaceAsync("events"); guard=shell.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title=="Unsaved changes" && d.Visible); guard.EmitSignal(ConfirmationDialog.SignalName.CustomAction,"discard"); await nav;
        await shell.RequestWorkspaceAsync("world"); await app.SelectAsync(npcKey); app.SetValue(npcKey,"Hp",System.Text.Json.Nodes.JsonValue.Create(int.Parse(initial)+4));
        nav=shell.RequestWorkspaceAsync("events"); guard=shell.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title=="Unsaved changes" && d.Visible); guard.EmitSignal(AcceptDialog.SignalName.Confirmed); await nav;
        if (shell.Active?.Id!="events" || app.HasUnsavedChanges) throw new Exception("Save did not complete world navigation.");
        await shell.RequestWorkspaceAsync("world"); await app.SelectAsync(npcKey); app.SetValue(npcKey,"Hp",System.Text.Json.Nodes.JsonValue.Create(int.Parse(initial))); if(!await app.SaveAsync()) throw new Exception("World dirty fixture restoration failed.");
    }
}
