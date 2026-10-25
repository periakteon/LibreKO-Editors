using Godot;
namespace LibreKO.Editors.Desktop;
public partial class ItemBoardTile : Control
{
    readonly NpcContentsView owner;readonly PanelContainer frame;readonly Label captionLabel;
    public string Kind {get;}
    public int Index {get;}
    public int ItemId {get;}
    public ItemBoardTile(NpcContentsView owner,string kind,int index,int itemId,string caption)
    {
        this.owner=owner;Kind=kind;Index=index;ItemId=itemId;Name=$"BoardTile_{kind}_{index}";CustomMinimumSize=new(46,60);MouseDefaultCursorShape=CursorShape.PointingHand;
        frame=Ui.Add(this,new PanelContainer{MouseFilter=MouseFilterEnum.Ignore});frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);frame.AddThemeStyleboxOverride("panel",Ui.Surface(3));var body=Ui.Column(frame);body.MouseFilter=MouseFilterEnum.Ignore;
        bool pool=kind=="drop"&&itemId is >=100 and <100_000_000;
        Texture2D? texture=null;if(itemId>0&&!pool)try{texture=LibreKO.ItemIcons.Get(itemId);}catch{}
        if(pool)Ui.Label(body,"POOL",10,Ui.Accent).MouseFilter=MouseFilterEnum.Ignore;
        else Ui.Add(body,new TextureRect{Texture=texture,CustomMinimumSize=new(40,40),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore});
        captionLabel=Ui.Label(body,caption,9,Ui.Muted);captionLabel.MouseFilter=MouseFilterEnum.Ignore;
        TooltipText=owner.Tooltip(itemId,kind);
    }
    public void SetMeaning(string text)=>TooltipText=text;
    public void SetCaption(string text)=>captionLabel.Text=text;
    public void Highlight(bool selected)=>frame.AddThemeStyleboxOverride("panel",Ui.Surface(3,selected));
    public override void _GuiInput(InputEvent input)
    {
        if(input is InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Left})
        {if(Kind=="palette")return;owner.SelectSlot(Index);AcceptEvent();}
    }
    public override Variant _GetDragData(Vector2 position)
    {
        if(ItemId<=0)return default;
        var preview=new Label{Text=TooltipText.Split('\n')[0]};SetDragPreview(preview);
        return owner.DragPayload(Kind,Index,ItemId);
    }
    public override bool _CanDropData(Vector2 position,Variant data)=>owner.CanDrop(Kind,Index,data);
    public override void _DropData(Vector2 position,Variant data)=>_ = owner.DropAsync(Kind,Index,data);
}
