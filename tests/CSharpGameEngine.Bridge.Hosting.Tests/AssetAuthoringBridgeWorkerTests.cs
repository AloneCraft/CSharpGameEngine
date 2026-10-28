using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.AssetAuthoring.Cli;

namespace CSharpGameEngine.Bridge.Hosting.Tests;

public class AssetAuthoringBridgeWorkerTests {
    [Fact]
    public void FontPreviewUsesTheCanonicalCodecWithoutWritingTheSource(){
        using var Directory=new TemporaryDirectory();
        var Font=new CSharpGameEngine.Rendering.FontAtlas(1024,2,0.8f,0.2f,0,0.1f,[new(65,0.6f,0,0,0.6f,0.8f,0,0,1,1)],Enumerable.Repeat((byte)200,2048).ToArray());
        var Source=Path.Combine(Directory.Path,"Test.cgefont");var Bytes=Font.ToBytes();File.WriteAllBytes(Source,Bytes);
        var Request=new AssetWorkspaceRequest{WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source}]};
        var RequestPath=Path.Combine(Directory.Path,"request.json");var ResultPath=Path.Combine(Directory.Path,"result.json");
        File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request));
        Assert.Equal(0,AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath).ExitCode);
        var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(File.ReadAllText(ResultPath));
        Assert.True(Result!.Success,Result.Diagnostic);Assert.Equal(Request.RequestId,Result.RequestId);
        var Preview=Result.FontPreview!;Assert.Equal(512,Preview.Width);Assert.Equal(2,Preview.Height);Assert.Equal(1024,Preview.SourceWidth);Assert.Equal(1,Preview.GlyphCount);
        Assert.All(Preview.Distance,static Pixel=>Assert.Equal(200,Pixel));Assert.Equal(Bytes,File.ReadAllBytes(Source));Assert.False(File.Exists(Source+".meta"));
        Assert.Null(AssetFontPreview.Validate(Preview));
        Assert.Equal(0.8f,Preview.Ascent);Assert.Equal(0.2f,Preview.Descent);Assert.Equal(0.1f,Preview.SpreadEm);
        var Glyph=Assert.Single(Preview.Glyphs);
        Assert.Equal(65,Glyph.CodePoint);Assert.Equal(0.6f,Glyph.Advance);Assert.Equal(0.6f,Glyph.PlaneRight);Assert.Equal(0.8f,Glyph.PlaneTop);Assert.Equal(1f,Glyph.U1);Assert.True(Glyph.HasQuad);
    }
    private static byte[]Wav(int SampleRate,int Channels,short[]Samples){
        using var Stream=new MemoryStream();
        using var Writer=new BinaryWriter(Stream);
        var DataBytes=Samples.Length*2;
        Writer.Write("RIFF"u8.ToArray());Writer.Write(36+DataBytes);Writer.Write("WAVE"u8.ToArray());
        Writer.Write("fmt "u8.ToArray());Writer.Write(16);Writer.Write((short)1);Writer.Write((short)Channels);Writer.Write(SampleRate);
        Writer.Write(SampleRate*Channels*2);Writer.Write((short)(Channels*2));Writer.Write((short)16);
        Writer.Write("data"u8.ToArray());Writer.Write(DataBytes);
        foreach(var Sample in Samples){ Writer.Write(Sample); }
        Writer.Flush();
        return Stream.ToArray();
    }
    [Fact]
    public void AudioPreviewReportsTheFormatAndNormalizedPeaksWithoutWritingTheSource(){
        using var Directory=new TemporaryDirectory();
        var Samples=Enumerable.Range(0,4800).Select(static Index=>(short)(Index<2400?Index*10:0)).ToArray();
        var Source=Path.Combine(Directory.Path,"Tone.wav");var Bytes=Wav(48000,1,Samples);File.WriteAllBytes(Source,Bytes);
        var Result=AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source}]});
        Assert.True(Result.Success,Result.Diagnostic);
        var Preview=Result.AudioPreview!;
        Assert.Equal(48000,Preview.SampleRate);Assert.Equal(1,Preview.Channels);Assert.Equal(16,Preview.BitsPerSample);Assert.Equal(4800,Preview.FrameCount);
        Assert.Equal(0.1f,Preview.DurationSeconds,3);
        Assert.Equal(2048,Preview.Peaks.Length);
        Assert.All(Preview.Peaks,static Peak=>Assert.InRange(Peak,0f,1f));
        Assert.True(Preview.Peaks[1000]>Preview.Peaks[100]);Assert.Equal(0f,Preview.Peaks[^1]);
        Assert.Equal(Bytes,File.ReadAllBytes(Source));Assert.False(File.Exists(Source+".meta"));
    }
    [Fact]
    public void AudioPreviewRoundTripsThroughTheBridgeEntryPoint(){
        using var Directory=new TemporaryDirectory();
        var Source=Path.Combine(Directory.Path,"Stereo.wav");File.WriteAllBytes(Source,Wav(44100,2,[short.MaxValue,0,short.MinValue,0,100,-100,0,0]));
        var Request=new AssetWorkspaceRequest{WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source}]};
        var RequestPath=Path.Combine(Directory.Path,"request.json");var ResultPath=Path.Combine(Directory.Path,"result.json");
        File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request));
        Assert.Equal(0,AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath).ExitCode);
        var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(File.ReadAllText(ResultPath));
        Assert.True(Result!.Success,Result.Diagnostic);Assert.Equal(Request.RequestId,Result.RequestId);
        Assert.Equal(2,Result.AudioPreview!.Channels);Assert.Equal(4,Result.AudioPreview.FrameCount);Assert.Equal(4,Result.AudioPreview.Peaks.Length);
        Assert.Equal(1f,Result.AudioPreview.Peaks[0],3);Assert.Equal(1f,Result.AudioPreview.Peaks[1],3);
    }
    [Theory]
    [InlineData("not a wave file")]
    [InlineData("")]
    public void AudioPreviewRejectsUnreadableFilesWithADiagnostic(string Content){
        using var Directory=new TemporaryDirectory();var Source=Path.Combine(Directory.Path,"Broken.wav");File.WriteAllText(Source,Content);
        var Result=AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source}]});
        Assert.False(Result.Success);Assert.NotEmpty(Result.Diagnostic);Assert.Null(Result.AudioPreview);
    }
    [Fact]
    public void AudioPreviewRejectsNonWavAndMalformedRequests(){
        using var Directory=new TemporaryDirectory();var Source=Path.Combine(Directory.Path,"Song.mp3");File.WriteAllBytes(Source,new byte[64]);
        Assert.False(AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source}]}).Success);
        var Wave=Path.Combine(Directory.Path,"Tone.wav");File.WriteAllBytes(Wave,Wav(8000,1,[1,2,3,4]));
        Assert.False(AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=3,RequestId=Guid.Empty,Sources=[new(){SourcePath=Wave}]}).Success);
        Assert.False(AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Wave}]}).Success);
        Assert.False(AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath="Tone.wav"}]}).Success);
        Assert.False(AssetAuthoringBridgeWorker.PreviewAudio(new(){WorkspaceCommand=3,RequestId=Guid.NewGuid(),Sources=[]}).Success);
    }
    [Fact]
    public void PeaksUseTheLargestAbsoluteAmplitudeAcrossChannelsAndNeverExceedTheFrameCount(){
        var Peaks=AssetAuthoringBridgeWorker.ComputePeaks([16384,-32768,0,8192],2,2048);
        Assert.Equal(2,Peaks.Length);
        Assert.Equal(new[]{ 1f,0.25f },Peaks);
        Assert.Empty(AssetAuthoringBridgeWorker.ComputePeaks([],1,2048));
        Assert.Equal(3,AssetAuthoringBridgeWorker.ComputePeaks([1,2,3],1,100).Length);
        Assert.Equal(2,AssetAuthoringBridgeWorker.ComputePeaks([1,2,3,4,5,6],1,2).Length);
    }
    [Fact]
    public void InvalidFontPreviewReturnsADiagnosticWithoutAnImage(){
        using var Directory=new TemporaryDirectory();var Source=Path.Combine(Directory.Path,"Broken.cgefont");File.WriteAllText(Source,"invalid");
        var Result=AssetAuthoringBridgeWorker.PreviewFont(new(){WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){SourcePath=Source}]});
        Assert.False(Result.Success);Assert.NotEmpty(Result.Diagnostic);Assert.Null(Result.FontPreview);
    }
    [Fact]
    public void EmptyFontReturnsACorrelatedDiagnosticAndDoesNotWriteTheSource(){
        using var Directory=new TemporaryDirectory();
        var Source=Path.Combine(Directory.Path,"Empty.cgefont");
        File.WriteAllBytes(Source,[]);
        var Request=new AssetWorkspaceRequest { WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){ SourcePath=Source }] };
        var RequestPath=Path.Combine(Directory.Path,"request.json");
        var ResultPath=Path.Combine(Directory.Path,"result.json");
        File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request));
        var Execution=AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath);
        Assert.True(Execution.ExitCode==0,Execution.Output);
        var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(File.ReadAllText(ResultPath))!;
        Assert.Equal(Request.RequestId,Result.RequestId);
        Assert.False(Result.Success);
        Assert.Contains("ASSET_FONT_PREVIEW_SIZE_INVALID",Result.Diagnostic);
        Assert.Null(Result.FontPreview);
        Assert.Empty(File.ReadAllBytes(Source));
    }
    [Fact]
    public void InvalidFontPreviewRequestReturnsADiagnostic(){
        var Request=new AssetWorkspaceRequest { WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){ SourcePath="relative.cgefont" }] };
        var Result=AssetAuthoringBridgeWorker.PreviewFont(Request);
        Assert.Equal(Request.RequestId,Result.RequestId);
        Assert.False(Result.Success);
        Assert.Contains("ASSET_FONT_PREVIEW_REQUEST_INVALID",Result.Diagnostic);
        Assert.Null(Result.FontPreview);
    }
    [Fact]
    public void InspectAndSave_RoundTripsThroughTypedFiles(){
        using TemporaryDirectory Directory=new();
        string Source=Path.Combine(Directory.Path,"mesh.glb");
        File.WriteAllBytes(Source,[0x67,0x6c,0x54,0x46]);

        AssetAuthoringResult Missing=Run(
            Directory.Path,Source,AssetAuthoringCommand.Inspect,string.Empty,string.Empty);
        Assert.Equal(AssetAuthoringState.MissingMetadata,Missing.State);

        AssetAuthoringResult Ready=Run(
            Directory.Path,Source,AssetAuthoringCommand.SaveMetadata,Missing.AssetId,string.Empty);
        Assert.Equal(AssetAuthoringState.Ready,Ready.State);
        Assert.Equal(Missing.AssetId,Ready.AssetId);
        Assert.True(File.Exists(Source+".meta"));
    }

    [Fact]
    public void MalformedRequest_FailsWithoutPublishingResult(){
        using TemporaryDirectory Directory=new();
        string RequestPath=Path.Combine(Directory.Path,"request.json");
        string ResultPath=Path.Combine(Directory.Path,"result.json");
        File.WriteAllText(RequestPath,"{");

        (int ExitCode,string Output)=AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath);

        Assert.NotEqual(0,ExitCode);
        Assert.NotEmpty(Output);
        Assert.False(File.Exists(ResultPath));
    }

    private static AssetAuthoringResult Run(
        string Root,string Source,AssetAuthoringCommand Command,string AssetId,string ExpectedHash){
        string RequestPath=Path.Combine(Root,Guid.NewGuid().ToString("N")+".request.json");
        string ResultPath=Path.Combine(Root,Guid.NewGuid().ToString("N")+".result.json");
        AssetAuthoringRequest Request=new(){
            RequestId=Guid.NewGuid(),Command=Command,RepositoryRoot=Root,SourcePath=Source,
            AssetId=AssetId,Importer=Command==AssetAuthoringCommand.SaveMetadata?"SharpGLTF":string.Empty,
            ExpectedMetadataSha256=ExpectedHash,
        };
        File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request));

        (int ExitCode,string Output)=AssetAuthoringBridgeWorker.Run(RequestPath,ResultPath);

        Assert.Equal(0,ExitCode);
        Assert.Empty(Output);
        AssetAuthoringResult?Result=JsonSerializer.Deserialize<AssetAuthoringResult>(
            File.ReadAllText(ResultPath));
        Assert.NotNull(Result);
        Assert.Null(AssetAuthoringProtocol.Validate(Request,Result));
        return Result;
    }

    private sealed class TemporaryDirectory:IDisposable {
        public TemporaryDirectory(){
            this.Path=System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),"CgeAssetAuthoringBridgeTests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Path);
        }
        public string Path{get;}
        public void Dispose()=>Directory.Delete(this.Path,true);
    }
}
