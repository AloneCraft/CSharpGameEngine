using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Graphics.D3D12;
using TerraFX.Interop.DirectX;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 環境変数で有効化する実行時の自動検証フック。描画が始まってから指定フレーム数を表示した後に、
/// その最終フレームをGPUからReadbackしてPNGへ保存し、ゲームループを正常終了させる。
/// 未設定時は何もしない。テンプレートやゲームのWARP Stageが、プロセス外からウィンドウを操作せずに
/// 描画結果を観測するために使う。<see cref="InputVariable"/>はフレーム番号で指定したキーを押下し、
/// <see cref="ReportVariable"/>は各フレームのカメラ位置をJSONで書き出す(操作結果の観測用)。
/// </summary>
public sealed class RuntimeAutomation {
    public const string FramesVariable="CGE_AUTOMATION_FRAMES";
    public const string CaptureVariable="CGE_AUTOMATION_CAPTURE";
    /// <summary><c>Code@Start-End</c>を<c>;</c>で区切った押下予定。StartからEnd直前までの表示フレームの間、押下し続ける。</summary>
    public const string InputVariable="CGE_AUTOMATION_INPUT";
    public const string ReportVariable="CGE_AUTOMATION_REPORT";
    public const string ReportSchema="cge-automation-report/1";
    private readonly List<Vector3?>CameraPositions=[];
    private RuntimeAutomation(int ExitAfterFrames,string?CapturePath,IReadOnlyList<RuntimeAutomationKeyHold>Input,string?ReportPath){
        this.ExitAfterFrames=ExitAfterFrames;
        this.CapturePath=CapturePath;
        this.Input=Input;
        this.ReportPath=ReportPath;
    }
    /// <summary>描画開始後に表示するフレーム数。最後のフレームがCapture対象になる。</summary>
    public int ExitAfterFrames { get; }
    public string?CapturePath { get; }
    public IReadOnlyList<RuntimeAutomationKeyHold>Input { get; }
    public string?ReportPath { get; }
    public int PresentedFrames { get;private set; }
    public bool Completed=>this.PresentedFrames>=this.ExitAfterFrames;
    public static RuntimeAutomation?FromEnvironment(){
        var FramesText=Environment.GetEnvironmentVariable(FramesVariable);
        if(string.IsNullOrWhiteSpace(FramesText)){
            return null;
        }
        if(!int.TryParse(FramesText,NumberStyles.None,CultureInfo.InvariantCulture,out var Frames)||Frames<1){
            throw new InvalidOperationException($"{FramesVariable} must be a positive integer, but was '{FramesText}'.");
        }
        var Capture=Environment.GetEnvironmentVariable(CaptureVariable);
        if(!TryParseInput(Environment.GetEnvironmentVariable(InputVariable),out var Input,out var InputError)){
            throw new InvalidOperationException($"{InputVariable} is invalid: {InputError}");
        }
        var Report=Environment.GetEnvironmentVariable(ReportVariable);
        return new RuntimeAutomation(Frames,string.IsNullOrWhiteSpace(Capture)?null:Path.GetFullPath(Capture),Input,string.IsNullOrWhiteSpace(Report)?null:Path.GetFullPath(Report));
    }
    /// <summary>Parses <see cref="InputVariable"/>; an empty value schedules nothing.</summary>
    public static bool TryParseInput(string?Text,out IReadOnlyList<RuntimeAutomationKeyHold>Holds,out string Error){
        List<RuntimeAutomationKeyHold>Result=[];
        Holds=Result;
        Error=string.Empty;
        foreach(var Entry in (Text??string.Empty).Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)){
            var At=Entry.LastIndexOf('@');
            var Dash=At<0?-1:Entry.IndexOf('-',At);
            if(At<=0||Dash<0
                ||!int.TryParse(Entry.AsSpan(At+1,Dash-At-1),NumberStyles.None,CultureInfo.InvariantCulture,out var Start)
                ||!int.TryParse(Entry.AsSpan(Dash+1),NumberStyles.None,CultureInfo.InvariantCulture,out var End)
                ||End<=Start||Entry.AsSpan(0,At).ContainsAny(' ','\t')){
                Error=$"'{Entry}' is not Code@Start-End with Start < End.";
                return false;
            }
            Result.Add(new RuntimeAutomationKeyHold(Entry[..At],Start,End));
        }
        return true;
    }
    /// <summary>Holds each scheduled key while the frame about to be presented is inside one of its ranges.</summary>
    internal void ApplyInput(Win32Window Window){
        var Frame=this.PresentedFrames;
        foreach(var Code in this.Input.Select(static Hold=>Hold.Code).Distinct(StringComparer.Ordinal)){
            Window.InjectKey(Code,this.Input.Any(Hold=>Hold.Code==Code&&Hold.Contains(Frame)));
        }
    }
    internal bool CapturesNextFrame=>this.CapturePath is not null&&this.PresentedFrames+1==this.ExitAfterFrames;
    internal void RecordPresented(RenderServer Renderer,Vector3?CameraPosition){
        var Captured=this.CapturesNextFrame;
        this.PresentedFrames++;
        this.CameraPositions.Add(CameraPosition);
        if(this.Completed&&this.ReportPath is not null){
            this.WriteReport(Renderer.ErrorOrWorseMessageCount);
        }
        if(!Captured){
            return;
        }
        var Frame=Renderer.LastCapturedFrame??throw new InvalidOperationException("The automation capture frame was not read back.");
        WritePng(Frame,this.CapturePath!);
        // The capture is the verification evidence, so the device diagnostics that qualify it are
        // reported next to it (D3D12 debug-layer errors turn a plausible image into a failed run).
        foreach(var Message in Renderer.DebugMessages.Where(static Message=>Message.IsErrorOrWorse).Take(20)){
            Console.Error.WriteLine("CGE-AUTOMATION-D3D12 "+Message);
        }
        Console.Error.WriteLine($"CGE-AUTOMATION-CAPTURE path={this.CapturePath} d3d12Errors={Renderer.ErrorOrWorseMessageCount}");
    }
    private void WriteReport(int D3D12Errors){
        var Directory=System.IO.Path.GetDirectoryName(this.ReportPath);
        if(!string.IsNullOrEmpty(Directory)){
            System.IO.Directory.CreateDirectory(Directory);
        }
        using var Output=File.Create(this.ReportPath!);
        using var Writer=new Utf8JsonWriter(Output,new JsonWriterOptions { Indented=true });
        Writer.WriteStartObject();
        Writer.WriteString("schema",ReportSchema);
        Writer.WriteNumber("frames",this.PresentedFrames);
        Writer.WriteNumber("d3d12Errors",D3D12Errors);
        // Frame-local camera positions (in the camera's reference frame), one entry per presented frame.
        Writer.WriteStartArray("cameraPositions");
        foreach(var Position in this.CameraPositions){
            if(Position is not{ }Value){
                Writer.WriteNullValue();
                continue;
            }
            Writer.WriteStartArray();
            Writer.WriteNumberValue(Value.X);
            Writer.WriteNumberValue(Value.Y);
            Writer.WriteNumberValue(Value.Z);
            Writer.WriteEndArray();
        }
        Writer.WriteEndArray();
        Writer.WriteEndObject();
    }
    /// <summary>R8G8B8A8のReadback結果を、行ピッチを除いた不透明RGBのPNGとして保存する。</summary>
    public static void WritePng(PixelReadbackResult Frame,string Path){
        if(Frame.Format!=DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM){
            throw new NotSupportedException($"Automation capture supports R8G8B8A8_UNORM only, but was {Frame.Format}.");
        }
        var Directory=System.IO.Path.GetDirectoryName(Path);
        if(!string.IsNullOrEmpty(Directory)){
            System.IO.Directory.CreateDirectory(Directory);
        }
        using MemoryStream Raw=new();
        using(ZLibStream Compressor=new(Raw,CompressionLevel.Fastest,true)){
            var Row=new byte[1+Frame.Width*3];
            for(var Y=0;Y<Frame.Height;Y++){
                var Source=(int)(Y*Frame.RowPitch);
                for(var X=0;X<Frame.Width;X++){
                    Row[1+X*3]=Frame.Pixels[Source+X*4];
                    Row[2+X*3]=Frame.Pixels[Source+X*4+1];
                    Row[3+X*3]=Frame.Pixels[Source+X*4+2];
                }
                Compressor.Write(Row);
            }
        }
        using FileStream Output=File.Create(Path);
        Output.Write([0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]);
        var Header=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(Header,Frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(Header.AsSpan(4),Frame.Height);
        Header[8]=8;
        Header[9]=2;
        WriteChunk(Output,"IHDR"u8,Header);
        WriteChunk(Output,"IDAT"u8,Raw.ToArray());
        WriteChunk(Output,"IEND"u8,[]);
    }
    private static void WriteChunk(Stream Output,ReadOnlySpan<byte>Type,ReadOnlySpan<byte>Data){
        Span<byte>Length=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(Length,Data.Length);
        Output.Write(Length);
        Output.Write(Type);
        Output.Write(Data);
        var Crc=Crc32(Crc32(0xFFFFFFFFu,Type),Data)^0xFFFFFFFFu;
        Span<byte>CrcBytes=stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(CrcBytes,Crc);
        Output.Write(CrcBytes);
    }
    private static uint Crc32(uint Crc,ReadOnlySpan<byte>Data){
        foreach(var Value in Data){
            Crc^=Value;
            for(var Bit=0;Bit<8;Bit++){
                Crc=(Crc&1)!=0?(Crc>>1)^0xEDB88320u:Crc>>1;
            }
        }
        return Crc;
    }
}
/// <summary>One scheduled key hold: pressed from <see cref="StartFrame"/> up to, not including, <see cref="EndFrame"/>.</summary>
public readonly record struct RuntimeAutomationKeyHold(string Code,int StartFrame,int EndFrame) {
    public bool Contains(int Frame)=>Frame>=this.StartFrame&&Frame<this.EndFrame;
}
