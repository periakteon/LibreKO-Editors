using Godot;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class WorldTerrainAliasView
{
    public static void Show(EditorSession session,int? zone)
    {
        if(zone==null){session.Shell.SetStatus("Select a map to choose its preview terrain.");return;}
        if(session.Sources.Profile is not {} profile){SourceSettingsView.Show(session);return;}
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title="Preview terrain · zone "+zone,MinSize=new(660,310),OkButtonText="Use terrain alias",DialogHideOnOk=false});
        var body=Ui.Column(d);body.Position=new(16,40);body.CustomMinimumSize=new(620,210);
        Ui.Label(body,"Choose the packed client terrain for this zone. This mapping is saved in the selected source profile; the server SMD mapping is edited in the database inspector.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var list=Ui.Add(body,new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});var stems=new List<string>();
        using var dir=DirAccess.Open("res://assets/terrain");
        foreach(var stem in (dir?.GetDirectories()??[]).Order(StringComparer.OrdinalIgnoreCase))
            if(Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/heightmap.png")){list.AddItem(stem);stems.Add(stem);}
        var error=Ui.Label(body,"",12,Ui.Accent);var generation=session.Generation;var sourceGeneration=session.Sources.Generation;
        d.Confirmed+=async()=>
        {
            if(list.Selected<0)return;d.Hide();if(!await session.Shell.CanLeaveAsync()){d.PopupCentered();return;}
            var ok=await session.RunAsync(OperationKind.Source,async ct=>
            {
                if(generation!=session.Generation||sourceGeneration!=session.Sources.Generation||session.Sources.Profile?.Id!=profile.Id)throw new InvalidOperationException("Database/source scope changed. Review the mapping again.");
                var aliases=profile.TerrainAliases?.ToDictionary(x=>x.Key,x=>x.Value)??[];aliases[zone.Value]=stems[list.Selected];var updated=profile with{TerrainAliases=aliases};
                await session.Sources.SelectAsync(updated,ct);session.Profile=session.Profile with{Sources=session.Profile.Sources!.Select(p=>p.Id==profile.Id?updated:p).ToArray()};ProfileStore.Save(session.ProfilePath,session.Profile);
            });
            if(ok){d.QueueFree();if(session.Shell.Active!=null)await session.Shell.Active.RefreshAsync();}else if(GodotObject.IsInstanceValid(d)){error.Text="Mapping could not be applied. Review the current source profile.";d.PopupCentered();}
        };
        d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
}
