using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>波形（区間ピーク）と再生位置を描き、クリックでシークを要求する。色は VS のテーマに追従する。</summary>
internal sealed class AssetWaveformView:FrameworkElement {
    internal static readonly DependencyProperty WaveBrushProperty=DependencyProperty.Register(nameof(WaveBrush),typeof(Brush),typeof(AssetWaveformView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    internal static readonly DependencyProperty BackBrushProperty=DependencyProperty.Register(nameof(BackBrush),typeof(Brush),typeof(AssetWaveformView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    internal static readonly DependencyProperty PlayheadBrushProperty=DependencyProperty.Register(nameof(PlayheadBrush),typeof(Brush),typeof(AssetWaveformView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    private float[]Peaks=[];
    private float Playhead;
    internal event Action<float>?SeekRequested;
    internal AssetWaveformView(){
        this.SetResourceReference(WaveBrushProperty,VsBrushes.WindowTextKey);
        this.SetResourceReference(BackBrushProperty,VsBrushes.WindowKey);
        this.SetResourceReference(PlayheadBrushProperty,VsBrushes.HighlightKey);
        this.MinHeight=96;
        this.ClipToBounds=true;
        this.MouseLeftButtonDown+=this.OnMouseDown;
    }
    internal Brush?WaveBrush { get=>(Brush?)this.GetValue(WaveBrushProperty);set=>this.SetValue(WaveBrushProperty,value);}
    internal Brush?BackBrush { get=>(Brush?)this.GetValue(BackBrushProperty);set=>this.SetValue(BackBrushProperty,value);}
    internal Brush?PlayheadBrush { get=>(Brush?)this.GetValue(PlayheadBrushProperty);set=>this.SetValue(PlayheadBrushProperty,value);}
    internal bool HasPeaks=>this.Peaks.Length>0;
    internal void SetPeaks(float[]Peaks){
        this.Peaks=Peaks??[];
        this.InvalidateVisual();
    }
    /// <summary>再生位置の割合（0..1）。</summary>
    internal void SetPlayhead(float Fraction){
        this.Playhead=Math.Min(1f,Math.Max(0f,Fraction));
        this.InvalidateVisual();
    }
    protected override void OnRender(DrawingContext Context){
        var Width=(float)this.ActualWidth;
        var Height=(float)this.ActualHeight;
        if(Width<=0f||Height<=0f){ return; }
        Context.DrawRectangle(this.BackBrush,null,new Rect(0,0,Width,Height));
        var Wave=this.WaveBrush;
        var Columns=AssetWaveform.Resample(this.Peaks,(int)Width);
        var Middle=Height/2f;
        for(var Column=0;Column<Columns.Length;Column++){
            var Half=Math.Max(1f,Columns[Column]*Middle);
            Context.DrawRectangle(Wave,null,new Rect(Column,Middle-Half,1,Half*2f));
        }
        Context.DrawRectangle(this.PlayheadBrush,null,new Rect(this.Playhead*Width,0,2,Height));
    }
    private void OnMouseDown(object Sender,MouseButtonEventArgs Args){
        var Width=(float)this.ActualWidth;
        if(Width<=0f){ return; }
        this.SeekRequested?.Invoke((float)Args.GetPosition(this).X/Width);
    }
}
