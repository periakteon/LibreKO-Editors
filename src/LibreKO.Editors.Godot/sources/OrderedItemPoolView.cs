using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;
namespace LibreKO.Editors.Desktop;
public partial class OrderedItemPoolView:VBoxContainer
{
    readonly EditorSession session;readonly Action<string> apply;readonly Func<bool> current;readonly VBoxContainer list;readonly Label caption;
    string raw="";List<string> members=[];int page;
    public OrderedItemPoolView(EditorSession session,string initial,Action<string> apply,Func<bool> current)
    {
        this.session=session;this.apply=apply;this.current=current;var bar=Ui.Add(this,new HFlowContainer());
        Ui.Button(bar,"Add named item…",()=>ReferencePicker.Show(session.Shell,session,new("items"),item=>{if(!current())return;members.Add(item.Key.Id.ToString());Commit();},current));
        Ui.Button(bar,"‹",()=>{if(page>0){page--;Render();}});Ui.Button(bar,"›",()=>{if((page+1)*25<members.Count){page++;Render();}});
        caption=Ui.Label(this,"",11,Ui.Muted);list=Ui.Column(this);SetRaw(initial);
    }
    public void SetRaw(string text){if(raw==text&&members.Count>0)return;raw=text;members=text.Length==0?[]:text.Split(',').ToList();page=Math.Clamp(page,0,Math.Max(0,(members.Count-1)/25));Render();}
    void Commit(){raw=string.Join(",",members);apply(raw);Render();}
    public override void _Ready()=>Render();
    void Render()
    {
        Ui.Clear(list);caption.Text=$"Ordered pool · {members.Count} members · duplicates retained · page {page+1}";
        for(int i=page*25;i<Math.Min(members.Count,(page+1)*25);i++)
        {
            int at=i;var row=Ui.Row(list);var token=members[i];var meaning=Ui.Label(row,$"{i+1}. {token}",11,Ui.Accent);meaning.SizeFlagsHorizontal=SizeFlags.ExpandFill;meaning.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            if(int.TryParse(token.Trim(),out var id)&&id>0)
            {
                Texture2D? texture=null;try{texture=LibreKO.ItemIcons.Get(id);}catch{}
                Ui.Add(row,new TextureRect{Texture=texture,CustomMinimumSize=new(28,28),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});
                session.ReferencePresenter.Bind(meaning,()=>new("items",id),()=>current()&&IsInstanceValid(this)&&IsInsideTree(),n=>meaning.Text=$"{at+1}. "+ReferenceLabels.Display(n));
            }
            Ui.Button(row,"↑",()=>{if(!current()||at==0)return;(members[at-1],members[at])=(members[at],members[at-1]);Commit();});
            Ui.Button(row,"↓",()=>{if(!current()||at+1>=members.Count)return;(members[at+1],members[at])=(members[at],members[at+1]);Commit();});
            Ui.Button(row,"×",()=>{if(!current())return;members.RemoveAt(at);Commit();});
        }
    }
}
