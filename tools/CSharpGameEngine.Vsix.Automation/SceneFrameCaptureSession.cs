using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
namespace CSharpGameEngine.Vsix.Automation;
internal sealed class SceneFrameCaptureSession:IDisposable {
    private const int TargetFrameMilliseconds=16;
    private readonly IntPtr SurfaceHandle;
    private readonly string EvidenceDirectory;
    private readonly string SceneName;
    private readonly object Sync=new();
    private readonly List<SceneFrameSample>Samples=[];
    private readonly List<string>RepresentativePaths=[];
    private readonly Stopwatch Clock=new();
    private readonly Thread Worker;
    private bool StopRequested;
    private Exception?Failure;
    private long ReadyElapsedMilliseconds=-1;
    internal SceneFrameCaptureSession(IntPtr SurfaceHandle,string EvidenceDirectory,string SceneName){
        if(SurfaceHandle==IntPtr.Zero){ throw new ArgumentException("A render surface handle is required.",nameof(SurfaceHandle)); }
        this.SurfaceHandle=SurfaceHandle;
        this.EvidenceDirectory=EvidenceDirectory;
        this.SceneName=SceneName;
        this.Worker=new Thread(this.CaptureLoop){ IsBackground=true,Name="CGE Scene frame capture" };
    }
    internal static IntPtr FindRenderSurface(int MainWindowHandle){
        IntPtr Result=IntPtr.Zero;
        EnumChildWindows(new IntPtr(MainWindowHandle),(Window,Parameter)=>{
            StringBuilder ClassName=new(256);
            _=GetClassName(Window,ClassName,ClassName.Capacity);
            if(string.Equals(ClassName.ToString(),"CSharpGameEngine.VSExtension.RenderSurface",StringComparison.Ordinal)){
                Result=Window;
                return false;
            }
            return true;
        },IntPtr.Zero);
        return Result;
    }
    internal void Start(){
        Directory.CreateDirectory(this.EvidenceDirectory);
        this.Clock.Start();
        this.Worker.Start();
    }
    internal void MarkReady()=>this.ReadyElapsedMilliseconds=this.Clock.ElapsedMilliseconds;
    internal SceneFrameCaptureResult StopAndAnalyze(long RequiredStableMilliseconds){
        lock(this.Sync){ this.StopRequested=true; }
        if(!this.Worker.Join(TimeSpan.FromSeconds(5))){
            throw new InvalidOperationException("Scene frame capture worker did not stop.");
        }
        if(this.Failure is not null){
            throw new InvalidOperationException("Scene frame capture failed.",this.Failure);
        }
        if(this.ReadyElapsedMilliseconds<0){
            throw new InvalidOperationException("Scene frame capture was never marked ready.");
        }
        SceneFrameSample[]Snapshot;
        string[]Representatives;
        lock(this.Sync){
            Snapshot=[..this.Samples];
            Representatives=[..this.RepresentativePaths];
        }
        SceneFrameSequenceAnalysis Analysis=SceneFrameSequenceAnalyzer.Analyze(
            Snapshot,this.ReadyElapsedMilliseconds,RequiredStableMilliseconds);
        string TimelinePath=Path.Combine(this.EvidenceDirectory,"timeline.json");
        File.WriteAllText(TimelinePath,JsonSerializer.Serialize(new SceneFrameTimelineEvidence {
            SceneName=this.SceneName,
            ReadyElapsedMilliseconds=this.ReadyElapsedMilliseconds,
            Analysis=Analysis,
            Samples=Snapshot,
        },new JsonSerializerOptions { WriteIndented=true })+Environment.NewLine);
        return new SceneFrameCaptureResult(Analysis,TimelinePath,Representatives);
    }
    public void Dispose(){
        lock(this.Sync){ this.StopRequested=true; }
        if(this.Worker.IsAlive){ this.Worker.Join(TimeSpan.FromSeconds(5)); }
    }
    private void CaptureLoop(){
        try {
            long NextRepresentative=0;
            long NextFrame=0;
            while(true){
                lock(this.Sync){ if(this.StopRequested){ return; } }
                long Elapsed=this.Clock.ElapsedMilliseconds;
                SceneFrameSample Sample;
                using(Bitmap?Frame=CaptureSurface(this.SurfaceHandle)){
                    Sample=Frame is null
                        ?new SceneFrameSample(Elapsed,false,0)
                        :new SceneFrameSample(Elapsed,HasRenderedVariation(Frame),ComputeSignature(Frame,out float AverageLuminance),AverageLuminance);
                    if(Frame is not null&&Elapsed>=NextRepresentative){
                        string PathValue=Path.Combine(this.EvidenceDirectory,
                            $"frame-{Elapsed:D8}.png");
                        using(Bitmap Representative=new(160,90,PixelFormat.Format32bppArgb))
                        using(Graphics RepresentativeGraphics=Graphics.FromImage(Representative)){
                            RepresentativeGraphics.DrawImage(Frame,0,0,Representative.Width,Representative.Height);
                            Representative.Save(PathValue,ImageFormat.Png);
                        }
                        lock(this.Sync){ this.RepresentativePaths.Add(PathValue); }
                        NextRepresentative=Elapsed+1000;
                    }
                }
                lock(this.Sync){ this.Samples.Add(Sample); }
                NextFrame+=TargetFrameMilliseconds;
                int Delay=checked((int)Math.Max(0,NextFrame-this.Clock.ElapsedMilliseconds));
                if(Delay>0){ Thread.Sleep(Delay); }
            }
        } catch(Exception Exception){
            this.Failure=Exception;
        }
    }
    private static Bitmap?CaptureSurface(IntPtr Handle){
        if(!GetWindowRect(Handle,out NativeRect Rect)){ return null; }
        int Width=Rect.Right-Rect.Left;
        int Height=Rect.Bottom-Rect.Top;
        if(Width<64||Height<64){ return null; }
        Bitmap Result=new(Width,Height,PixelFormat.Format32bppArgb);
        try {
            using(Graphics GraphicsValue=Graphics.FromImage(Result)){
                GraphicsValue.CopyFromScreen(Rect.Left,Rect.Top,0,0,new Size(Width,Height));
            }
            return Result;
        } catch {
            Result.Dispose();
            throw;
        }
    }
    private static bool HasRenderedVariation(Bitmap Frame){
        HashSet<int>Colors=[];
        int StepX=Math.Max(1,Frame.Width/24);
        int StepY=Math.Max(1,Frame.Height/24);
        for(int Y=StepY/2;Y<Frame.Height;Y+=StepY){
            for(int X=StepX/2;X<Frame.Width;X+=StepX){
                Color ColorValue=Frame.GetPixel(X,Y);
                Colors.Add((ColorValue.R/8<<16)|(ColorValue.G/8<<8)|ColorValue.B/8);
            }
        }
        if(Colors.Count<2){ return false; }
        int[]Values=[..Colors];
        for(int LeftIndex=0;LeftIndex<Values.Length;LeftIndex++){
            for(int RightIndex=LeftIndex+1;RightIndex<Values.Length;RightIndex++){
                int Left=Values[LeftIndex];
                int Right=Values[RightIndex];
                int Distance=Math.Abs((Left>>16&0xFF)-(Right>>16&0xFF))
                    +Math.Abs((Left>>8&0xFF)-(Right>>8&0xFF))
                    +Math.Abs((Left&0xFF)-(Right&0xFF));
                if(Distance>=8){ return true; }
            }
        }
        return false;
    }
    private static ulong ComputeSignature(Bitmap Frame,out float AverageLuminance){
        int[]Luminance=new int[64];
        int Sum=0;
        for(int Y=0;Y<8;Y++){
            for(int X=0;X<8;X++){
                Color ColorValue=Frame.GetPixel(
                    Math.Min(Frame.Width-1,(X*2+1)*Frame.Width/16),
                    Math.Min(Frame.Height-1,(Y*2+1)*Frame.Height/16));
                int Value=ColorValue.R*3+ColorValue.G*6+ColorValue.B;
                Luminance[Y*8+X]=Value;
                Sum+=Value;
            }
        }
        int Average=Sum/Luminance.Length;
        // R*3+G*6+B spans 0-2550; report the mean on the 0-255 scale.
        AverageLuminance=Sum/(float)Luminance.Length/10f;
        ulong Signature=0;
        for(int Index=0;Index<Luminance.Length;Index++){
            if(Luminance[Index]>=Average){ Signature|=1UL<<Index; }
        }
        return Signature;
    }
    private delegate bool EnumWindowsCallback(IntPtr Window,IntPtr Parameter);
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect { internal int Left;internal int Top;internal int Right;internal int Bottom; }
    [DllImport("user32.dll")]private static extern bool EnumChildWindows(IntPtr Parent,EnumWindowsCallback Callback,IntPtr Parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr Window,StringBuilder ClassName,int MaximumCount);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr Window,out NativeRect Rect);
}
internal sealed class SceneFrameCaptureResult {
    internal SceneFrameCaptureResult(SceneFrameSequenceAnalysis Analysis,string TimelinePath,IReadOnlyList<string>RepresentativePaths){
        this.Analysis=Analysis;
        this.TimelinePath=TimelinePath;
        this.RepresentativePaths=RepresentativePaths;
    }
    internal SceneFrameSequenceAnalysis Analysis { get; }
    internal string TimelinePath { get; }
    internal IReadOnlyList<string>RepresentativePaths { get; }
}
internal sealed class SceneFrameTimelineEvidence {
    public string SceneName { get; set; }=string.Empty;
    public long ReadyElapsedMilliseconds { get; set; }
    public SceneFrameSequenceAnalysis?Analysis { get; set; }
    public IReadOnlyList<SceneFrameSample>Samples { get; set; }=[];
}
