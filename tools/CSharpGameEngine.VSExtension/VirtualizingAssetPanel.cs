using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>固定サイズのアセットタイル。表示中の行だけを生成し、先読みの行を 1 行含める。</summary>
public sealed class VirtualizingAssetPanel:VirtualizingPanel,IScrollInfo {
    public VirtualizingAssetPanel(){ this.Unloaded+=(_,_)=>this.CancelVisibleThumbnails(); }
    private void CancelVisibleThumbnails(){
        foreach(UIElement Child in this.InternalChildren){ if(Child is FrameworkElement{DataContext:AssetWorkspaceRow Row}){ Row.CancelThumbnail(); } }
    }
    private const float TileWidth=138;
    private const float TileHeight=130;
    private float Offset;
    private Size Extent;
    private Size Viewport;
    private int Columns=1;
    [ExternalNumericAdapter]
    protected override Size MeasureOverride(Size AvailableSize){
        var Owner=ItemsControl.GetItemsOwner(this);
        var Count=Owner?.Items.Count??0;
        var Width=double.IsInfinity(AvailableSize.Width)?600:AvailableSize.Width;
        var Height=double.IsInfinity(AvailableSize.Height)?280:AvailableSize.Height;
        this.Columns=Math.Max(1,(int)(Width/TileWidth));
        this.Viewport=new Size(Width,Height);
        this.Extent=new Size(Width,Math.Ceiling((double)Count/this.Columns)*TileHeight);
        this.Offset=FloatMath.FromExternal(Math.Max(0,Math.Min(this.Offset,Math.Max(0,this.Extent.Height-Height))));
        this.ScrollOwner?.InvalidateScrollInfo();
        var First=Math.Max(0,(int)(this.Offset/TileHeight)*this.Columns);
        var Last=Math.Min(Count-1,((int)((this.Offset+Height)/TileHeight)+2)*this.Columns-1);
        var Generator=this.ItemContainerGenerator;
        for(var Index=this.InternalChildren.Count-1;Index>=0;Index--){
            var Position=new GeneratorPosition(Index,0);
            var ItemIndex=Generator.IndexFromGeneratorPosition(Position);
            if(ItemIndex<First||ItemIndex>Last){ if(this.InternalChildren[Index] is FrameworkElement{DataContext:AssetWorkspaceRow Row}){ Row.CancelThumbnail(); }Generator.Remove(Position,1);this.RemoveInternalChildRange(Index,1); }
        }
        if(Last>=First){
            var Position=Generator.GeneratorPositionFromIndex(First);
            var ChildIndex=Position.Offset==0?Position.Index:Position.Index+1;
            using(Generator.StartAt(Position,GeneratorDirection.Forward,true)){
                for(var Index=First;Index<=Last;Index++,ChildIndex++){
                    var Child=(UIElement)Generator.GenerateNext(out var New);
                    if(New){ this.InsertInternalChild(ChildIndex,Child);Generator.PrepareItemContainer(Child); }
                    Child.Measure(new Size(TileWidth,TileHeight));
                }
            }
        }
        return new Size(Width,Height);
    }
    protected override Size ArrangeOverride(Size FinalSize){
        for(var Index=0;Index<this.InternalChildren.Count;Index++){
            var ItemIndex=this.ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(Index,0));
            this.InternalChildren[Index].Arrange(new Rect(ItemIndex%this.Columns*TileWidth,ItemIndex/this.Columns*TileHeight-this.Offset,TileWidth,TileHeight));
        }
        return FinalSize;
    }
    protected override void OnItemsChanged(object Sender,ItemsChangedEventArgs Args){
        this.CancelVisibleThumbnails();this.RemoveInternalChildRange(0,this.InternalChildren.Count);this.InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int Index)=>this.SetVerticalOffset(Index/this.Columns*TileHeight);
    public bool CanHorizontallyScroll{get;set;}
    public bool CanVerticallyScroll{get;set;}
    [ExternalNumericAdapter]
    public double ExtentWidth=>this.Extent.Width;
    [ExternalNumericAdapter]
    public double ExtentHeight=>this.Extent.Height;
    [ExternalNumericAdapter]
    public double ViewportWidth=>this.Viewport.Width;
    [ExternalNumericAdapter]
    public double ViewportHeight=>this.Viewport.Height;
    [ExternalNumericAdapter]
    public double HorizontalOffset=>0;
    [ExternalNumericAdapter]
    public double VerticalOffset=>this.Offset;
    public ScrollViewer?ScrollOwner{get;set;}
    public void LineUp()=>this.SetVerticalOffset(this.Offset-TileHeight);
    public void LineDown()=>this.SetVerticalOffset(this.Offset+TileHeight);
    public void MouseWheelUp()=>this.LineUp();
    public void MouseWheelDown()=>this.LineDown();
    public void PageUp()=>this.SetVerticalOffset(this.Offset-this.Viewport.Height);
    public void PageDown()=>this.SetVerticalOffset(this.Offset+this.Viewport.Height);
    [ExternalNumericAdapter]
    public void SetVerticalOffset(double Value){ if(double.IsNaN(Value)||double.IsInfinity(Value)){ return; }this.Offset=FloatMath.FromExternal(Math.Max(0,Math.Min(Value,Math.Max(0,this.Extent.Height-this.Viewport.Height))));this.InvalidateMeasure();this.ScrollOwner?.InvalidateScrollInfo(); }
    public void SetHorizontalOffset(double Value){ }
    public void LineLeft(){ }
    public void LineRight(){ }
    public void MouseWheelLeft(){ }
    public void MouseWheelRight(){ }
    public void PageLeft(){ }
    public void PageRight(){ }
    public Rect MakeVisible(Visual Visual,Rect Rectangle){
        if(Visual is UIElement Child){
            var Index=this.InternalChildren.IndexOf(Child);
            if(Index>=0){ this.BringIndexIntoView(this.ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(Index,0))); }
        }
        return Rectangle;
    }
}
