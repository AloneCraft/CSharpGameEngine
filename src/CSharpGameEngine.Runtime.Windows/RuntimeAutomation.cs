using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using CSharpGameEngine.Graphics.D3D12;
using TerraFX.Interop.DirectX;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 環境変数で有効化する実行時の自動検証フック。描画が始まってから指定フレーム数を表示した後に、
/// その最終フレームをGPUからReadbackしてPNGへ保存し、ゲームループを正常終了させる。
/// 未設定時は何もしない。テンプレートやゲームのWARP Stageが、プロセス外からウィンドウを操作せずに
/// 描画結果を観測するために使う。
/// </summary>
public sealed class RuntimeAutomation {
    public const string FramesVariable="CGE_AUTOMATION_FRAMES";
    public const string CaptureVariable="CGE_AUTOMATION_CAPTURE";
    private RuntimeAutomation(int ExitAfterFrames,string?CapturePath){
        this.ExitAfterFrames=ExitAfterFrames;
        this.CapturePath=CapturePath;
    }
    /// <summary>描画開始後に表示するフレーム数。最後のフレームがCapture対象になる。</summary>
    public int ExitAfterFrames { get; }
    public string?CapturePath { get; }
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
        return new RuntimeAutomation(Frames,string.IsNullOrWhiteSpace(Capture)?null:Path.GetFullPath(Capture));
    }
    internal bool CapturesNextFrame=>this.CapturePath is not null&&this.PresentedFrames+1==this.ExitAfterFrames;
    internal void RecordPresented(RenderServer Renderer){
        var Captured=this.CapturesNextFrame;
        this.PresentedFrames++;
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
