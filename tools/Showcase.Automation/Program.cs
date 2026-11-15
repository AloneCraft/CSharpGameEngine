using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace CSharpGameEngine.Showcase.Automation;
public enum ShowcaseAutomationExitCode { Success=0,OracleMismatch=1,InfrastructureError=2 }
public sealed record ShowcaseAutomationOptions(string Configuration,string Adapter,string Locale,int TimeoutSeconds,string EvidenceDirectory);
public static class ShowcaseAutomationArguments {
    public static bool TryParse(string[]Args,out ShowcaseAutomationOptions?Options,out string Error){
        Options=null;
        Error=string.Empty;
        if(Args.Length==0||!string.Equals(Args[0],"run",StringComparison.OrdinalIgnoreCase)){ Error="Expected 'run'.";return false; }
        var Values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(var Index=1;Index<Args.Length;Index+=2){
            if(Index+1>=Args.Length||!Args[Index].StartsWith("--",StringComparison.Ordinal)){ Error=$"Missing value for '{Args[Index]}'.";return false; }
            Values[Args[Index][2..]]=Args[Index+1];
        }
        var Configuration=Values.GetValueOrDefault("configuration","Debug");
        var Adapter=Values.GetValueOrDefault("adapter","warp");
        var Locale=Values.GetValueOrDefault("locale","auto");
        var Evidence=Values.GetValueOrDefault("evidence-dir",Path.Combine(".artifacts","Showcase"));
        if(Configuration is not("Debug" or "Release")){ Error="Configuration must be Debug or Release.";return false; }
        if(Adapter is not("warp" or "hardware" or "default")){ Error="Adapter must be warp, hardware, or default.";return false; }
        if(Locale is not("auto" or "ja-JP" or "en-US")){ Error="Locale must be auto, ja-JP, or en-US.";return false; }
        if(!int.TryParse(Values.GetValueOrDefault("timeout-seconds","180"),out var Timeout)||Timeout is <10 or >3600){ Error="Timeout must be between 10 and 3600 seconds.";return false; }
        Options=new ShowcaseAutomationOptions(Configuration,Adapter,Locale,Timeout,Evidence);
        return true;
    }
}
public sealed record ShowcaseEvidence(
    string Schema,string RunId,string Configuration,string Adapter,string Locale,string StartedUtc,string CompletedUtc,
    string Result,int ExitCode,int OwnedProcessId,bool NormalShutdown,string?Screenshot,string?PanelSnapshot,
    IReadOnlyList<string>Observations,IReadOnlyList<string>Failures);
internal static class Program {
    private const uint WmKeyDown=0x0100;
    private const uint WmKeyUp=0x0101;
    private const uint WmClose=0x0010;
    private static int Main(string[]Args){
        if(!ShowcaseAutomationArguments.TryParse(Args,out var Options,out var Error)||Options is null){ Console.Error.WriteLine(Error);return (int)ShowcaseAutomationExitCode.InfrastructureError; }
        return (int)new ShowcaseAutomation(Options).Run();
    }
    private sealed class ShowcaseAutomation(ShowcaseAutomationOptions Options) {
        private readonly List<string>Observations=[];
        private readonly List<string>Failures=[];
        private readonly Stopwatch Timeout=Stopwatch.StartNew();
        private Process?OwnedProcess;
        private string RunDirectory=string.Empty;
        private DateTime StartedUtc;
        public ShowcaseAutomationExitCode Run(){
            this.StartedUtc=DateTime.UtcNow;
            var RunId=this.StartedUtc.ToString("yyyyMMdd-HHmmss-fff")+"-"+Environment.ProcessId;
            this.RunDirectory=Path.GetFullPath(Path.Combine(Options.EvidenceDirectory,RunId));
            Directory.CreateDirectory(this.RunDirectory);
            var Result=ShowcaseAutomationExitCode.InfrastructureError;
            var NormalShutdown=false;
            string?Screenshot=null;
            string?PanelSnapshot=null;
            try {
                this.Preflight();
                var RepositoryRoot=FindRepositoryRoot();
                this.BuildApplication(RepositoryRoot);
                var Executable=Path.Combine(RepositoryRoot,"UserEcsProject","bin","x64",Options.Configuration,"net10.0-windows10.0.19041.0","win-x64","UserEcsProject.exe");
                this.OwnedProcess=this.StartApplication(Executable);
                var MainWindow=this.WaitForMainWindow();
                var Panel=this.WaitForPanel(MainWindow);
                this.Observe("startup",Panel,"page=ecs");
                this.VerifyPage(MainWindow,Panel,0x31,"ecs");
                this.VerifyPage(MainWindow,Panel,0x32,"rendering");
                this.VerifyPage(MainWindow,Panel,0x33,"physics");
                this.VerifyPage(MainWindow,Panel,0x34,"progress");
                this.VerifyPauseAndStep(MainWindow,Panel);
                this.VerifyProgressPaging(MainWindow,Panel);
                this.VerifyPanelVisibility(MainWindow,Panel);
                PanelSnapshot=Path.Combine(this.RunDirectory,"dashboard-panel.bmp");
                this.RequestPanelSnapshot(PanelSnapshot);
                Screenshot=Path.Combine(this.RunDirectory,"showcase-combined.bmp");
                WindowBitmap.SaveCombined(MainWindow,Panel,PanelSnapshot,Screenshot);
                this.Observations.Add("panel-capture="+GetWindowTextValue(Panel));
                NormalShutdown=this.CloseOwnedProcess(MainWindow);
                Result=this.Failures.Count==0?ShowcaseAutomationExitCode.Success:ShowcaseAutomationExitCode.OracleMismatch;
            } catch(Exception Exception){
                this.Failures.Add(Exception.GetType().Name+": "+Exception.Message);
                Result=Exception is ShowcaseOracleException?ShowcaseAutomationExitCode.OracleMismatch:ShowcaseAutomationExitCode.InfrastructureError;
            } finally {
                if(this.OwnedProcess is { HasExited:false }){
                    this.OwnedProcess.Kill();
                    this.OwnedProcess.WaitForExit();
                    this.Observations.Add("forced-owned-process-termination");
                }
                var Evidence=new ShowcaseEvidence(
                    "csharp-game-engine-showcase-evidence-v1",RunId,Options.Configuration,Options.Adapter,Options.Locale,
                    this.StartedUtc.ToString("O"),DateTime.UtcNow.ToString("O"),Result.ToString(),(int)Result,
                    this.OwnedProcess?.Id??0,NormalShutdown,
                    Screenshot is null?null:Path.GetFileName(Screenshot),PanelSnapshot is null?null:Path.GetFileName(PanelSnapshot),
                    this.Observations,this.Failures);
                File.WriteAllText(Path.Combine(this.RunDirectory,"evidence.json"),JsonSerializer.Serialize(Evidence,new JsonSerializerOptions { WriteIndented=true })+"\n",new UTF8Encoding(false));
                File.WriteAllLines(Path.Combine(this.RunDirectory,"activity.log"),this.Observations.Concat(this.Failures.Select(static Failure=>"FAIL "+Failure)),new UTF8Encoding(false));
                Console.WriteLine(Path.Combine(this.RunDirectory,"evidence.json"));
            }
            return Result;
        }
        private void Preflight(){
            if(!OperatingSystem.IsWindows()||!Environment.UserInteractive){ throw new InvalidOperationException("An interactive Windows desktop is required."); }
            var Desktop=OpenInputDesktop(0,false,0x0100);
            if(Desktop==0){ throw new InvalidOperationException("The input desktop is unavailable or locked."); }
            _=CloseDesktop(Desktop);
        }
        private void BuildApplication(string RepositoryRoot){
            var StartInfo=new ProcessStartInfo("dotnet"){
                WorkingDirectory=RepositoryRoot,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,
            };
            StartInfo.ArgumentList.Add("build");
            StartInfo.ArgumentList.Add("UserEcsProject/UserEcsProject.csproj");
            StartInfo.ArgumentList.Add("--configuration");
            StartInfo.ArgumentList.Add(Options.Configuration);
            StartInfo.ArgumentList.Add("-p:Platform=x64");
            StartInfo.ArgumentList.Add("--no-restore");
            using var Build=Process.Start(StartInfo)??throw new InvalidOperationException("Failed to start dotnet build.");
            var StandardOutput=Build.StandardOutput.ReadToEndAsync();
            var StandardError=Build.StandardError.ReadToEndAsync();
            Build.WaitForExit(120_000);
            if(!Build.HasExited){ Build.Kill(true);throw new InvalidOperationException("UserEcsProject build timed out."); }
            var Output=StandardOutput.GetAwaiter().GetResult()+StandardError.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(this.RunDirectory,"build.log"),Output,new UTF8Encoding(false));
            if(Build.ExitCode!=0){ throw new InvalidOperationException($"UserEcsProject build failed with exit code {Build.ExitCode}."); }
            this.Observations.Add("build=success");
        }
        private Process StartApplication(string Executable){
            if(!File.Exists(Executable)){ throw new FileNotFoundException("Showcase executable was not produced.",Executable); }
            var StartInfo=new ProcessStartInfo(Executable){ WorkingDirectory=Path.GetDirectoryName(Executable)!,UseShellExecute=false };
            StartInfo.Environment["CGE_GRAPHICS_ADAPTER"]=Options.Adapter;
            StartInfo.Environment["CGE_D3D12_DEBUG_LAYER"]="1";
            StartInfo.Environment["CGE_SHOWCASE_LOCALE"]=Options.Locale;
            StartInfo.Environment["CGE_SHOWCASE_EVIDENCE_DIR"]=this.RunDirectory;
            var Process=System.Diagnostics.Process.Start(StartInfo)??throw new InvalidOperationException("Failed to start UserEcsProject.");
            this.Observations.Add($"owned-pid={Process.Id}");
            return Process;
        }
        private nint WaitForMainWindow(){
            while(this.WithinTimeout()){
                this.ThrowIfExited();
                this.OwnedProcess!.Refresh();
                if(this.OwnedProcess.MainWindowHandle!=0){ return this.OwnedProcess.MainWindowHandle; }
                Thread.Sleep(50);
            }
            throw new TimeoutException("The Showcase main window did not appear.");
        }
        private nint WaitForPanel(nint MainWindow){
            while(this.WithinTimeout()){
                this.ThrowIfExited();
                var Panel=FindChildByClass(MainWindow,"CSharpGameEngine.ShowcaseDashboard");
                if(Panel!=0&&GetWindowTextValue(Panel).StartsWith("CGE|",StringComparison.Ordinal)){ return Panel; }
                Thread.Sleep(50);
            }
            throw new TimeoutException("The Showcase dashboard child window did not appear.");
        }
        private void VerifyPage(nint MainWindow,nint Panel,int VirtualKey,string Page){
            SendKey(MainWindow,VirtualKey);
            var Title=this.WaitForTitle(Panel,$"page={Page}");
            this.Observations.Add($"page-{Page}={Title}");
        }
        private void VerifyPauseAndStep(nint MainWindow,nint Panel){
            SendKey(MainWindow,0x50);
            var Paused=this.WaitForTitle(Panel,"playback=paused");
            var Tick=ReadLong(Paused,"tick");
            Thread.Sleep(250);
            var Stable=ReadLong(GetWindowTextValue(Panel),"tick");
            if(Stable!=Tick){ throw new ShowcaseOracleException($"Pause advanced FixedTick from {Tick} to {Stable}."); }
            SendKey(MainWindow,0x4F);
            var Stepped=this.WaitForTick(Panel,Tick+1);
            Thread.Sleep(150);
            var StepStable=ReadLong(GetWindowTextValue(Panel),"tick");
            if(StepStable!=Tick+1){ throw new ShowcaseOracleException($"Step expected {Tick+1} but observed {StepStable}."); }
            this.Observations.Add($"pause-step={Tick}->{ReadLong(Stepped,"tick")}");
            SendKey(MainWindow,0x50);
            _=this.WaitForTitle(Panel,"playback=running");
        }
        private void VerifyProgressPaging(nint MainWindow,nint Panel){
            SendKey(MainWindow,0x34);
            _=this.WaitForTitle(Panel,"page=progress");
            SendKey(MainWindow,0x27);
            var Paged=this.WaitForTitle(Panel,"index=4");
            SendKey(MainWindow,0x28);
            var Layered=this.WaitForTitle(Panel,"layer=L1");
            this.Observations.Add($"progress-paging={Paged} -> {Layered}");
        }
        private void VerifyPanelVisibility(nint MainWindow,nint Panel){
            SendKey(MainWindow,0x70);
            this.WaitForVisibility(Panel,false);
            SendKey(MainWindow,0x70);
            this.WaitForVisibility(Panel,true);
            this.Observations.Add("panel-hide-show=success");
        }
        private bool CloseOwnedProcess(nint MainWindow){
            _=PostMessage(MainWindow,WmClose,0,0);
            if(this.OwnedProcess!.WaitForExit(5000)){ this.Observations.Add("shutdown=normal");return true; }
            this.Failures.Add("Normal shutdown exceeded five seconds.");
            return false;
        }
        private void RequestPanelSnapshot(string SnapshotPath){
            File.WriteAllText(Path.Combine(this.RunDirectory,"dashboard-capture.request"),"capture\n",new UTF8Encoding(false));
            while(this.WithinTimeout()){
                this.ThrowIfExited();
                if(File.Exists(SnapshotPath)&&new FileInfo(SnapshotPath).Length>54){ this.Observations.Add("panel-dib-capture=success");return; }
                Thread.Sleep(20);
            }
            throw new TimeoutException("The runtime did not publish the dashboard DIB snapshot.");
        }
        private string WaitForTitle(nint Panel,string Expected){
            while(this.WithinTimeout()){
                this.ThrowIfExited();
                var Title=GetWindowTextValue(Panel);
                if(Title.Contains(Expected,StringComparison.Ordinal)){ return Title; }
                Thread.Sleep(20);
            }
            throw new ShowcaseOracleException($"Dashboard title never contained '{Expected}'. Last='{GetWindowTextValue(Panel)}'.");
        }
        private string WaitForTick(nint Panel,long Expected){
            while(this.WithinTimeout()){
                this.ThrowIfExited();
                var Title=GetWindowTextValue(Panel);
                if(ReadLong(Title,"tick")==Expected){ return Title; }
                Thread.Sleep(20);
            }
            throw new ShowcaseOracleException($"FixedTick never reached exactly {Expected}.");
        }
        private void WaitForVisibility(nint Panel,bool Expected){
            while(this.WithinTimeout()){
                if(IsWindowVisible(Panel)==Expected){ return; }
                Thread.Sleep(20);
            }
            throw new ShowcaseOracleException($"Dashboard visibility did not become {Expected}.");
        }
        private void Observe(string Label,nint Panel,string Expected){
            while(this.WithinTimeout()){
                this.ThrowIfExited();
                var Title=GetWindowTextValue(Panel);
                if(Title.Contains(Expected,StringComparison.Ordinal)&&ReadLong(Title,"paint")>0){ this.Observations.Add($"{Label}={Title}");return; }
                Thread.Sleep(20);
            }
            throw new ShowcaseOracleException($"Dashboard never reported a successful GDI text draw for '{Expected}'.");
        }
        private bool WithinTimeout()=>this.Timeout.Elapsed<TimeSpan.FromSeconds(Options.TimeoutSeconds);
        private void ThrowIfExited(){ if(this.OwnedProcess!.HasExited){ throw new InvalidOperationException($"UserEcsProject exited early with code {this.OwnedProcess.ExitCode}."); } }
        private static string FindRepositoryRoot(){
            var DirectoryValue=new DirectoryInfo(AppContext.BaseDirectory);
            while(DirectoryValue is not null&&!File.Exists(Path.Combine(DirectoryValue.FullName,"CSharpGameEngine.slnx"))){ DirectoryValue=DirectoryValue.Parent; }
            return DirectoryValue?.FullName??throw new DirectoryNotFoundException("CSharpGameEngine.slnx was not found above the automation executable.");
        }
    }
    private sealed class ShowcaseOracleException(string Message):Exception(Message);
    private static long ReadLong(string Title,string Key){
        var Prefix=Key+"=";
        var Part=Title.Split('|').FirstOrDefault(Part=>Part.StartsWith(Prefix,StringComparison.Ordinal));
        return Part is not null&&long.TryParse(Part[Prefix.Length..],out var Value)?Value:-1;
    }
    private static void SendKey(nint Window,int VirtualKey){
        if(!PostMessage(Window,WmKeyDown,(nuint)VirtualKey,1)||!PostMessage(Window,WmKeyUp,(nuint)VirtualKey,unchecked((nint)(int)0xC0000001))){ throw new InvalidOperationException($"Failed to post key 0x{VirtualKey:X2}."); }
    }
    private static string GetWindowTextValue(nint Window){
        var Length=GetWindowTextLength(Window);
        var Builder=new StringBuilder(Length+1);
        _=GetWindowText(Window,Builder,Builder.Capacity);
        return Builder.ToString();
    }
    private static nint FindChildByClass(nint Parent,string ClassName){
        nint Found=0;
        _=EnumChildWindows(Parent,(Window,_)=>{
            var Builder=new StringBuilder(128);
            _=GetClassName(Window,Builder,Builder.Capacity);
            if(string.Equals(Builder.ToString(),ClassName,StringComparison.Ordinal)){ Found=Window;return false; }
            return true;
        },0);
        return Found;
    }
    private static class WindowBitmap {
        private sealed record Frame(int Width,int Height,int OriginX,int OriginY,byte[]Bytes);
        public static void SaveCombined(nint MainWindow,nint Panel,string PanelPath,string PathValue){
            var Main=Capture(MainWindow,false);
            var Dashboard=Read(Panel,PanelPath);
            var OffsetX=Dashboard.OriginX-Main.OriginX;
            var OffsetY=Dashboard.OriginY-Main.OriginY;
            for(var Y=0;Y<Dashboard.Height;Y++){
                var DestinationY=OffsetY+Y;
                if(DestinationY<0||DestinationY>=Main.Height){ continue; }
                var SourceX=Math.Max(0,-OffsetX);
                var DestinationX=Math.Max(0,OffsetX);
                var Count=Math.Min(Dashboard.Width-SourceX,Main.Width-DestinationX);
                if(Count<=0){ continue; }
                Buffer.BlockCopy(Dashboard.Bytes,(Y*Dashboard.Width+SourceX)*4,Main.Bytes,(DestinationY*Main.Width+DestinationX)*4,Count*4);
            }
            Write(PathValue,Main);
        }
        private static Frame Capture(nint Window,bool PrintClient){
            if(!GetWindowRect(Window,out var Rect)){ throw new InvalidOperationException("GetWindowRect failed."); }
            var Width=Math.Max(1,Rect.Right-Rect.Left);
            var Height=Math.Max(1,Rect.Bottom-Rect.Top);
            var WindowDc=GetWindowDC(Window);
            var MemoryDc=CreateCompatibleDC(WindowDc);
            var BitmapInfo=new NativeBitmapInfo { Header=new BitmapInfoHeader { Size=40,Width=Width,Height=-Height,Planes=1,BitCount=32,Compression=0,SizeImage=Width*Height*4 } };
            var Bitmap=CreateDIBSection(WindowDc,ref BitmapInfo,0,out var Bits,0,0);
            if(Bitmap==0||Bits==0){ throw new InvalidOperationException("CreateDIBSection failed."); }
            var Old=SelectObject(MemoryDc,Bitmap);
            try {
                if(PrintClient){ _=SendMessage(Window,0x0318,MemoryDc,4|8|16); }
                else if(!PrintWindow(Window,MemoryDc,2)){ throw new InvalidOperationException("PrintWindow failed."); }
                var Bytes=new byte[BitmapInfo.Header.SizeImage];
                Marshal.Copy(Bits,Bytes,0,Bytes.Length);
                return new Frame(Width,Height,Rect.Left,Rect.Top,Bytes);
            } finally {
                _=SelectObject(MemoryDc,Old);_=DeleteObject(Bitmap);_=DeleteDC(MemoryDc);_=ReleaseDC(Window,WindowDc);
            }
        }
        private static void Write(string PathValue,Frame FrameValue){
            using var Stream=File.Create(PathValue);
            using var Writer=new BinaryWriter(Stream,Encoding.UTF8,true);
            Writer.Write((byte)'B');Writer.Write((byte)'M');Writer.Write(54+FrameValue.Bytes.Length);Writer.Write((short)0);Writer.Write((short)0);Writer.Write(54);
            Writer.Write(40);Writer.Write(FrameValue.Width);Writer.Write(-FrameValue.Height);Writer.Write((short)1);Writer.Write((short)32);Writer.Write(0);Writer.Write(FrameValue.Bytes.Length);Writer.Write(0);Writer.Write(0);Writer.Write(0);Writer.Write(0);Writer.Write(FrameValue.Bytes);
        }
        private static Frame Read(nint Window,string PathValue){
            if(!GetWindowRect(Window,out var Rect)){ throw new InvalidOperationException("GetWindowRect failed for dashboard snapshot."); }
            using var Stream=File.OpenRead(PathValue);
            using var Reader=new BinaryReader(Stream,Encoding.UTF8,true);
            if(Reader.ReadByte()!='B'||Reader.ReadByte()!='M'){ throw new InvalidDataException("Dashboard snapshot is not a BMP."); }
            Stream.Position=10;
            var PixelOffset=Reader.ReadInt32();
            Stream.Position=18;
            var Width=Reader.ReadInt32();
            var SignedHeight=Reader.ReadInt32();
            Stream.Position=28;
            if(Reader.ReadInt16()!=32){ throw new InvalidDataException("Dashboard snapshot is not 32-bit BGRA."); }
            var Height=Math.Abs(SignedHeight);
            Stream.Position=PixelOffset;
            var Bytes=Reader.ReadBytes(checked(Width*Height*4));
            if(Bytes.Length!=Width*Height*4){ throw new EndOfStreamException("Dashboard snapshot pixel payload is truncated."); }
            if(SignedHeight>0){
                var Row=new byte[Width*4];
                for(var Top=0;Top<Height/2;Top++){
                    var Bottom=Height-1-Top;
                    Buffer.BlockCopy(Bytes,Top*Row.Length,Row,0,Row.Length);
                    Buffer.BlockCopy(Bytes,Bottom*Row.Length,Bytes,Top*Row.Length,Row.Length);
                    Buffer.BlockCopy(Row,0,Bytes,Bottom*Row.Length,Row.Length);
                }
            }
            HashSet<int>SampledColors=[];
            for(var Offset=0;Offset<Bytes.Length;Offset+=64){ SampledColors.Add(Bytes[Offset]|(Bytes[Offset+1]<<8)|(Bytes[Offset+2]<<16)); }
            if(SampledColors.Count<8){ throw new ShowcaseOracleException($"Dashboard DIB contains only {SampledColors.Count} sampled colors."); }
            return new Frame(Width,Height,Rect.Left,Rect.Top,Bytes);
        }
    }
    private delegate bool EnumWindowsProcedure(nint Window,nint Parameter);
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect { public int Left;public int Top;public int Right;public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfoHeader { public int Size;public int Width;public int Height;public short Planes;public short BitCount;public int Compression;public int SizeImage;public int XPelsPerMeter;public int YPelsPerMeter;public int ColorsUsed;public int ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)]private struct NativeBitmapInfo { public BitmapInfoHeader Header;public uint Color; }
    [DllImport("user32.dll")]private static extern bool EnumChildWindows(nint Parent,EnumWindowsProcedure Procedure,nint Parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(nint Window,StringBuilder ClassName,int Maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint Window,StringBuilder Text,int Maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowTextLength(nint Window);
    [DllImport("user32.dll")]private static extern bool PostMessage(nint Window,uint Message,nuint WParam,nint LParam);
    [DllImport("user32.dll")]private static extern nint SendMessage(nint Window,uint Message,nint WParam,nint LParam);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint Window);
    [DllImport("user32.dll")]private static extern nint OpenInputDesktop(uint Flags,bool Inherit,uint Access);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(nint Desktop);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint Window,out NativeRect Rect);
    [DllImport("user32.dll")]private static extern nint GetWindowDC(nint Window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(nint Window,nint Dc);
    [DllImport("user32.dll")]private static extern bool PrintWindow(nint Window,nint Dc,uint Flags);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleDC(nint Dc);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleBitmap(nint Dc,int Width,int Height);
    [DllImport("gdi32.dll")]private static extern nint CreateDIBSection(nint Dc,ref NativeBitmapInfo Info,uint Usage,out nint Bits,nint Section,uint Offset);
    [DllImport("gdi32.dll")]private static extern nint SelectObject(nint Dc,nint Object);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint Object);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint Dc);
    [DllImport("gdi32.dll")]private static extern bool BitBlt(nint Destination,int X,int Y,int Width,int Height,nint Source,int SourceX,int SourceY,int Operation);
    [DllImport("gdi32.dll")]private static extern int GetDIBits(nint Dc,nint Bitmap,uint Start,uint Lines,[Out]byte[]Bits,ref BitmapInfoHeader Info,uint Usage);
}
