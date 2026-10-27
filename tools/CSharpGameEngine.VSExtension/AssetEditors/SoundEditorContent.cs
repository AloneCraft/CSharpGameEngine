using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Sound Wave Editor（.wav と、再生だけできる .ogg／.mp3／.flac）。中央に波形と再生位置（クリックでシーク）、下に再生・停止・ループ、
/// 右に形式（サンプルレート・チャンネル・ビット深度・長さ）と Details。形式と波形ピークは Asset Authoring ブリッジが
/// ゲームと同じ <c>WavReader</c> で読んだ要約（<c>WorkspaceCommand=3</c>）で、ゲームが読めない形式はその旨を表示する。
/// 再生は WPF の <see cref="MediaPlayer"/> で、波形の編集・加工は外部ツールの範囲（Asset-Workspace 仕様）。
/// </summary>
internal sealed class SoundEditorContent:AssetEditorContent {
    private readonly AssetWaveformView Waveform=new();
    private readonly AssetDetailsView DetailsView;
    private readonly Grid CenterRoot=new();
    private readonly Grid DetailsRoot=new();
    private readonly StackPanel Facts=new(){ Margin=new Thickness(8,6,8,6) };
    private readonly TextBlock Message=new(){ HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24) };
    private readonly TextBlock TimeText=new(){ VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0) };
    private readonly Button PlayButton;
    private readonly CheckBox LoopBox;
    private readonly MediaPlayer Player=new();
    private readonly DispatcherTimer Timer=new(DispatcherPriority.Background){ Interval=TimeSpan.FromMilliseconds(50) };
    private readonly UIElement[]Toolbar;
    private CancellationTokenSource Loading=new();
    private JoinableTask?LoadingTask;
    private float DurationSeconds;
    private bool Playing;
    private bool Disposed;
    internal SoundEditorContent(string FilePath):base(FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView=new AssetDetailsView(FilePath);
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(this.Facts,0);
        Grid.SetRow(this.DetailsView,1);
        this.DetailsRoot.Children.Add(this.Facts);
        this.DetailsRoot.Children.Add(this.DetailsView);
        this.PlayButton=AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_Play"),this.TogglePlay);
        this.LoopBox=new CheckBox { Content=VsixText.Get("AssetEditor_Loop"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0),Focusable=false };
        this.Toolbar=[this.PlayButton,AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_Stop"),this.Stop),this.LoopBox,this.TimeText];
        this.Message.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.TimeText.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.CenterRoot.Children.Add(this.Waveform);
        this.CenterRoot.Children.Add(this.Message);
        this.Waveform.SeekRequested+=this.OnSeekRequested;
        this.Player.MediaOpened+=this.OnMediaOpened;
        this.Player.MediaEnded+=this.OnMediaEnded;
        this.Player.MediaFailed+=this.OnMediaFailed;
        this.Timer.Tick+=this.OnTick;
        this.StartLoading();
    }
    internal override UIElement Center=>this.CenterRoot;
    internal override UIElement?Details=>this.DetailsRoot;
    internal override IReadOnlyList<UIElement>ToolbarItems=>this.Toolbar;
    private bool IsWav=>System.IO.Path.GetExtension(this.FilePath).Equals(".wav",StringComparison.OrdinalIgnoreCase);
    private void StartLoading(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Loading.Cancel();
        this.Loading.Dispose();
        this.Loading=new CancellationTokenSource();
        var Token=this.Loading.Token;
        var Path=this.FilePath;
        this.Facts.Children.Clear();
        this.DurationSeconds=0f;
        this.Waveform.SetPeaks([]);
        this.Waveform.SetPlayhead(0f);
        this.TimeText.Text=AssetWaveform.FormatTime(0f)+" / "+AssetWaveform.FormatTime(0f);
        this.Message.Visibility=Visibility.Collapsed;
        this.OpenPlayer(Path);
        if(!this.IsWav){
            this.Message.Text=VsixText.Get("AssetEditor_SoundNotRead");
            this.Message.Visibility=Visibility.Visible;
            this.Facts.Children.Add(Line(VsixText.Get("AssetEditor_SoundNotRead")));
            this.SetStatus(VsixText.Get("AssetEditor_SoundNotRead"));
            return;
        }
        this.SetStatus(VsixText.Get("AssetEditor_SoundLoading"));
        this.LoadingTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            try {
                var Context=CgeProjectContext.Resolve(Path)??throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound"));
                var Request=new AssetWorkspaceRequest { WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new AssetWorkspaceSource { SourcePath=Path }] };
                var Result=await Task.Run(()=>AssetAuthoringHostClient.InspectWorkspace(Request,Context.WorkspaceRoot),Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Token.IsCancellationRequested||Result.AudioPreview is not{ }Preview){ return; }
                this.DurationSeconds=Preview.DurationSeconds;
                this.Waveform.SetPeaks(Preview.Peaks);
                this.ShowFacts(Preview);
                this.UpdateTime();
                this.SetStatus(VsixText.Get("AssetEditor_SoundReady"));
            } catch(OperationCanceledException){
            } catch(Exception Error){
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Token.IsCancellationRequested){ return; }
                var Text=VsixText.Format("AssetEditor_SoundFailed",Error.Message);
                this.Message.Text=Text;
                this.Message.Visibility=Visibility.Visible;
                this.Facts.Children.Add(Line(Text));
                this.SetStatus(Text);
            }
        });
    }
    private void OpenPlayer(string Path){
        this.Playing=false;
        this.Timer.Stop();
        this.Player.Stop();
        this.Player.Open(new Uri(Path));
        this.PlayButton.Content=VsixText.Get("AssetEditor_Play");
    }
    private void ShowFacts(AssetAudioPreview Preview){
        var Culture=CultureInfo.CurrentCulture;
        this.Facts.Children.Clear();
        this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_SoundSampleRate",Preview.SampleRate.ToString("N0",Culture))));
        this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_SoundChannels",VsixText.Get(Preview.Channels==1?"AssetEditor_SoundMono":"AssetEditor_SoundStereo"))));
        if(Preview.BitsPerSample>0){ this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_SoundBits",Preview.BitsPerSample.ToString(Culture)))); }
        this.Facts.Children.Add(Line(VsixText.Format("AssetEditor_SoundDuration",AssetWaveform.FormatTime(Preview.DurationSeconds))));
    }
    private void TogglePlay(){
        if(this.Disposed){ return; }
        if(this.Playing){
            this.Player.Pause();
            this.Playing=false;
            this.Timer.Stop();
            this.PlayButton.Content=VsixText.Get("AssetEditor_Play");
            return;
        }
        this.Player.Play();
        this.Playing=true;
        this.Timer.Start();
        this.PlayButton.Content=VsixText.Get("AssetEditor_Pause");
    }
    private void Stop(){
        if(this.Disposed){ return; }
        this.Player.Stop();
        this.Playing=false;
        this.Timer.Stop();
        this.PlayButton.Content=VsixText.Get("AssetEditor_Play");
        this.UpdateTime();
    }
    private float Duration=>this.DurationSeconds>0f?this.DurationSeconds:this.Player.NaturalDuration.HasTimeSpan?SecondsOf(this.Player.NaturalDuration.TimeSpan):0f;
    /// <summary>TimeSpan を秒（float）にする。<c>TotalSeconds</c> は double なので、刻み（long）から求める。</summary>
    private static float SecondsOf(TimeSpan Span)=>Span.Ticks/(float)TimeSpan.TicksPerSecond;
    private void UpdateTime(){
        var Duration=this.Duration;
        var Position=SecondsOf(this.Player.Position);
        this.Waveform.SetPlayhead(AssetWaveform.Fraction(Position,Duration));
        this.TimeText.Text=AssetWaveform.FormatTime(Position)+" / "+AssetWaveform.FormatTime(Duration);
    }
    private void OnSeekRequested(float Fraction){
        var Duration=this.Duration;
        if(Duration<=0f){ return; }
        this.Player.Position=TimeSpan.FromSeconds(Math.Min(1f,Math.Max(0f,Fraction))*Duration);
        this.UpdateTime();
        this.RequestFocus();
    }
    private void OnTick(object?Sender,EventArgs Args)=>this.UpdateTime();
    private void OnMediaOpened(object?Sender,EventArgs Args){
        if(!this.Disposed){ this.UpdateTime(); }
    }
    private void OnMediaEnded(object?Sender,EventArgs Args){
        if(this.Disposed){ return; }
        if(this.LoopBox.IsChecked==true){
            this.Player.Position=TimeSpan.Zero;
            this.Player.Play();
            return;
        }
        this.Stop();
    }
    private void OnMediaFailed(object?Sender,ExceptionEventArgs Args){
        if(this.Disposed){ return; }
        var Text=VsixText.Format("AssetEditor_SoundPlaybackFailed",Args.ErrorException?.Message??string.Empty);
        this.Playing=false;
        this.Timer.Stop();
        this.PlayButton.Content=VsixText.Get("AssetEditor_Play");
        this.SetStatus(Text);
    }
    private static TextBlock Line(string Text){
        var Block=new TextBlock { Text=Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2) };
        Block.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        return Block;
    }
    internal override bool HandleKeyDown(Key Key,ModifierKeys Modifiers){
        if(Key==Key.Space&&Modifiers==ModifierKeys.None){ this.TogglePlay();return true; }
        return false;
    }
    internal override bool HandleNativeShortcut(Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.DetailsView.HandleNativeShortcut(Key);
    }
    protected override void OnReload(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView.Retarget(this.FilePath);
        this.StartLoading();
    }
    public override void Dispose(){
        this.Disposed=true;
        this.Timer.Stop();
        this.Timer.Tick-=this.OnTick;
        this.Player.MediaOpened-=this.OnMediaOpened;
        this.Player.MediaEnded-=this.OnMediaEnded;
        this.Player.MediaFailed-=this.OnMediaFailed;
        this.Player.Close();
        this.Loading.Cancel();
        this.Loading.Dispose();
        this.Waveform.SeekRequested-=this.OnSeekRequested;
        this.DetailsView.Dispose();
    }
}
