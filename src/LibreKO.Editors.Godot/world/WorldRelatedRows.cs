using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public partial class WorldRelatedRows : VBoxContainer
{
    readonly WorldWorkspace workspace;
    readonly OptionButton relations;
    readonly ItemList list;
    readonly Label caption;
    WorldKey? owner;
    int page, serial;
    WorldRowSnapshot[] rows = [];
    public WorldRelatedRows(WorldWorkspace workspace)
    {
        this.workspace = workspace;
        Ui.Heading(this, "RELATED RECORDS", 11).AddThemeColorOverride("font_color", Ui.Accent);
        relations = Ui.Add(this, new OptionButton { FitToLongestItem = false }); relations.ItemSelected += index => { page = 0; _ = LoadPageAsync(); };
        Ui.Add(this, new ActivityIndicator(workspace.Session.Loading, "related"));
        list = Ui.Add(this, new ItemList { AllowReselect=true,SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 85), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        list.ItemSelected+=index=>{if(owner?.Table==WorldTable.ZoneInfos&&Selected() is {} spawn&&spawn.Key.Table==WorldTable.NpcPositions)_=workspace.FocusRelatedSpawnAsync(spawn);};
        list.ItemActivated+=index=>{if(Selected() is {} spawn&&spawn.Key.Table==WorldTable.NpcPositions)_=workspace.FocusRelatedSpawnAsync(spawn);};
        var tools = Ui.Row(this); Ui.Button(tools, "Open", async () => { if (Selected() is { } row) await workspace.Session.Shell.OpenTargetAsync(new("world",row.Key.Table.ToString(),new JsonObject{["id"]=row.Key.Id,["zone"]=row.Key.Zone,["isMonster"]=row.Key.IsMonster},workspace.Session.HistoryRoot)); });
        Ui.Button(tools,"Show on map",async()=>{if(Selected() is {} spawn&&spawn.Key.Table==WorldTable.NpcPositions)await workspace.FocusRelatedSpawnAsync(spawn);}).TooltipText="Zoom to this spawn's exact X/Z position in its zone. Double-click a placement to jump there.";
        caption = Ui.Label(this, "Select a record to inspect its dependencies", 11, Ui.Muted); caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Ui.Button(tools, "Edit together", async () => { if (Selected() is { } row) { await workspace.IncludeAsync(row); caption.Text = "Added to this database draft · " + row.Key.Id; } });
        Ui.Button(tools, "‹", () => { if (page > 0) { page--; _ = LoadPageAsync(); } }); Ui.Button(tools, "›", () => { if (rows.Length == 25) { page++; _ = LoadPageAsync(); } });
    }
    WorldRowSnapshot? Selected() => list.GetSelectedItems().Length == 1 ? rows.ElementAtOrDefault(list.GetSelectedItems()[0]) : null;
    public async Task LoadAsync(WorldKey? key)
    {
        serial++; owner = key; page = 0; relations.Clear(); list.Clear(); rows = [];
        foreach (var relation in key == null ? [] : WorldRules.Relations(key.Table)) relations.AddItem(relation.Replace('-', ' '));
        relations.Visible = relations.ItemCount > 0; if (relations.ItemCount > 0) { relations.Select(0); await LoadPageAsync(); }
        else caption.Text = "No supported dependent relation for this record. Reference fields link to named related records.";
    }
    async Task LoadPageAsync()
    {
        if (owner == null || !workspace.Session.Connected || relations.Selected < 0) return;
        using var activity = workspace.Session.Loading.Begin("related", "Loading related records…");
        var selectedOwner = owner; var request = ++serial; var generation = workspace.Session.Generation;
        var relation = WorldRules.Relations(owner.Table)[relations.Selected];
        var query = relation switch
        {
            "zone-spawns" => new WorldQuery(WorldTable.NpcPositions, Page: page, Size: 25, Zone: owner.Id),
            "zone-objects" => new WorldQuery(WorldTable.MapObjects, Page: page, Size: 25, Zone: owner.Id),
            "zone-events" => new WorldQuery(WorldTable.GameEvents, Page: page, Size: 25, Zone: owner.Id),
            "npc-spawns" => new WorldQuery(WorldTable.NpcPositions, Page: page, Size: 25, IsMonster: owner.IsMonster, ParentId: owner.Id),
            "npc-exchanges" => new WorldQuery(WorldTable.ItemExchanges, Page: page, Size: 25, ParentId: owner.Id),
            "drop-users" => new WorldQuery(WorldTable.Npcs, Page: page, Size: 25, IsMonster: owner.IsMonster, ParentId: owner.Id),
            _ => null
        };
        try
        {
            Page<WorldRowSnapshot> result;
            if (query != null) result = await workspace.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list", query);
            else
            {
                var membership = await workspace.Session.Client.SendAsync<WorldMembership>("world.membership", new { owner, relation }); var subset = new List<WorldRowSnapshot>();
                foreach (var key in membership.BeforeKeys.Skip(page * 25).Take(25)) subset.Add(await workspace.Session.Client.SendAsync<WorldRowSnapshot>("world.get", key));
                result = new(subset, membership.BeforeKeys.Count, page, 25);
            }
            if (request != serial || generation != workspace.Session.Generation || selectedOwner != owner || !IsInstanceValid(this)) return;
            rows = result.Rows.ToArray(); list.Clear(); foreach (var row in rows) list.AddItem(WorldWorkspace.RowLabel(row)); caption.Text = result.Total + " related records · page " + (page + 1) + (relation == "npc-exchanges" ? " · numeric ID matches either prototype type" : "");
            var scope = workspace.Session.References.Scope;
            var refs = rows.SelectMany(r => r.Values.Select(v => WorldRules.ReferenceFor(r.Key.Table, v.Key, r.Values))).Where(x => x != null).Select(x => x!).Concat(rows.Where(r=>r.Key.Table==WorldTable.NpcPositions).Select(r=>new ReferenceIdentity("zones",LibreKO.Editors.Core.FieldValueParser.ReadInt32(r.Values["ZoneId"])))).Distinct().ToArray();
            var names = await workspace.Session.References.ResolveBatchAsync(refs);
            if (request != serial || scope != workspace.Session.References.Scope || !IsInstanceValid(list)) return;
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var reference = row.Values.Select(v => WorldRules.ReferenceFor(row.Key.Table, v.Key, row.Values)).FirstOrDefault(r => r?.Kind == "npcs");
                var label=WorldWorkspace.RowLabel(row)+(reference==null?"":" · "+EventRewardView.Describe(reference,names));
                if(row.Key.Table==WorldTable.NpcPositions)
                {
                    var v=row.Values;var zone=new ReferenceIdentity("zones",LibreKO.Editors.Core.FieldValueParser.ReadInt32(v["ZoneId"]));
                    label=$"Spawn {row.Key.Id} · {EventRewardView.Describe(zone,names)}\nX {v["LeftX"]}…{v["RightX"]} · Z {v["TopZ"]}…{v["BottomZ"]} · count {v["NumNPC"]} · respawn {v["RegTime"]}s · patrol {v["Path"]}";
                    if(reference!=null)label+="\n"+EventRewardView.Describe(reference,names);
                }
                list.SetItemText(i,label);list.SetItemTooltip(i,label+"\nSelect a zone spawn to focus it; double-click or Show on map to open other placements.");
            }
        }
        catch (Exception e) { if (request == serial && IsInstanceValid(caption)) caption.Text = "Related lookup unavailable · " + e.Message; }
    }
}
