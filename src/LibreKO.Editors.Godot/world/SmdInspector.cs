using System.Globalization;
using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class SmdInspector
{
    public static void Show(EditorSession session,SmdMapLayers layers)
    {
        var doc=layers.Document;var kind=layers.SelectedKind;int ordinal=layers.SelectedOrdinal;
        var count=kind switch{"Warps"=>doc.Warps.Count,"Regene"=>doc.Regene.Count,_=>doc.Objects.Count};if(ordinal<0||ordinal>=count)return;
        var d=Ui.Add(session.Shell,new ConfirmationDialog{Title=$"SMD {kind} · source row {ordinal}",DialogHideOnOk=false,OkButtonText="Update source draft",MinSize=new(620,630)});
        var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(580,0);Ui.Heading(body,"SERVER MAP SOURCE",14);Ui.Label(body,layers.Snapshot.Key.RelativePath,12,Ui.Muted);
        var fields=new Dictionary<string,LineEdit>();
        void Field(string key,object value)=>fields[key]=Ui.Field(body,key,key,Convert.ToString(value,CultureInfo.InvariantCulture)!);
        var p=layers.SelectedPosition();var row=Ui.Row(body);foreach(var (key,value) in new[]{("X",p.X),("Y",p.Y),("Z",p.Z)})fields[key]=Ui.Field(row,key,key,value.ToString(CultureInfo.InvariantCulture));
        if(kind=="Warps"){var v=doc.Warps[ordinal];Field("ID",v.Id);Field("Name",v.Name);Field("Announcement",v.Announcement);Field("Fee",v.Fee);Field("Zone ID",v.Zone);Field("Radius",v.Radius);Field("Nation",v.Nation);}
        else if(kind=="Regene"){var v=doc.Regene[ordinal];Field("Area X",v.AreaX);Field("Area Z",v.AreaZ);}
        else {var v=doc.Objects[ordinal];Field("Belong",v.Belong);Field("Index",v.Index);Field("Type",v.Type);Field("Control target",v.ControlTarget);Field("Status",v.Status);}
        var error=Ui.Label(body,"",12,Ui.Accent);
        float F(string k)=>float.Parse(fields[k].Text,CultureInfo.InvariantCulture);short S(string k)=>short.Parse(fields[k].Text,CultureInfo.InvariantCulture);
        d.Confirmed+=()=>{try{layers.Edit(candidate=>
        {
            var point=new MapPoint(F("X"),F("Y"),F("Z"));
            if(kind=="Warps")candidate.Warps[ordinal]=candidate.Warps[ordinal] with{Id=S("ID"),Name=fields["Name"].Text,Announcement=fields["Announcement"].Text,Fee=uint.Parse(fields["Fee"].Text,CultureInfo.InvariantCulture),Zone=S("Zone ID"),Position=point,Radius=F("Radius"),Nation=S("Nation")};
            else if(kind=="Regene")candidate.Regene[ordinal]=candidate.Regene[ordinal] with{Position=point,AreaX=F("Area X"),AreaZ=F("Area Z")};
            else candidate.Objects[ordinal]=candidate.Objects[ordinal] with{Belong=int.Parse(fields["Belong"].Text,CultureInfo.InvariantCulture),Index=S("Index"),Type=S("Type"),ControlTarget=S("Control target"),Status=S("Status"),Position=point};
        });d.Hide();d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
    }
}
