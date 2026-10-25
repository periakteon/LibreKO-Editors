using Godot;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    Vector3 sceneryFocus;
    bool sceneryRefreshing;
    int sceneryJob;
    public Task SceneryReady {get;private set;}=Task.CompletedTask;
    void RefreshSceneryIfNeeded()
    {
        if(!Active||!Loaded||Stem==null||loading==null||loading.IsCancellationRequested||sceneryRefreshing||focus.DistanceSquaredTo(sceneryFocus)<250*250)return;
        SceneryReady=RefreshSceneryAsync();
    }
    async Task RefreshSceneryAsync()
    {
        var serial=loadSerial;var job=++sceneryJob;var token=loading!.Token;var stem=Stem!;var target=focus;sceneryRefreshing=true;Node3D? staged=null;
        try
        {
            await ToSignal(GetTree().CreateTimer(.25),SceneTreeTimer.SignalName.Timeout);token.ThrowIfCancellationRequested();if(serial!=loadSerial||!Active)return;
            staged=new Node3D{Name="SceneryRegion"};layers["Scenery"].AddChild(staged);await WorldSceneryPreview.BuildAsync(staged,stem,new(-target.X,target.Y,target.Z),token);
            token.ThrowIfCancellationRequested();if(serial!=loadSerial||!Active)return;
            foreach(var child in layers["Scenery"].GetChildren())if(child!=staged){layers["Scenery"].RemoveChild(child);child.QueueFree();}
            staged=null;sceneryFocus=target;Ui.Clear(layers["Water"]);Ui.Clear(layers["Effects"]);await BuildWaterAndEffectsAsync(stem,token);
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(serial==loadSerial&&Active)status.Text="Scenery region unavailable · "+e.Message;}
        finally
        {
            if(staged!=null&&IsInstanceValid(staged)){staged.GetParent()?.RemoveChild(staged);staged.QueueFree();}
            if(job==sceneryJob){sceneryRefreshing=false;if(serial==loadSerial&&Active)RefreshSceneryIfNeeded();}
        }
    }
}
