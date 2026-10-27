using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Import.AssetAuthoring.Cli;

internal static class AssetAuthoringBridgeWorker {
    private const int MaximumRequestBytes=32*1024*1024;
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=false };

    public static(int ExitCode,string Output)Run(string RequestPath,string ResultPath){
        try {
            byte[]RequestBytes=ReadBoundedRequest(RequestPath);
            using(var Envelope=JsonDocument.Parse(RequestBytes)){
                if(Envelope.RootElement.TryGetProperty("ModelCommand",out _)){
                    var Model=JsonSerializer.Deserialize<AssetModelRequest>(RequestBytes,JsonOptions)??throw new ArgumentException("Model request required.");
                    var ModelResult=CSharpGameEngine.Import.Fbx.FbxModelCompiler.Execute(Model);
                    WriteResultAtomically(ResultPath,JsonSerializer.SerializeToUtf8Bytes(ModelResult,JsonOptions));return(0,string.Empty);
                }
                if(Envelope.RootElement.TryGetProperty("ImageCommand",out _)){
                    var Image=JsonSerializer.Deserialize<AssetImageRequest>(RequestBytes,JsonOptions)??throw new ArgumentException("Image request required.");
                    var ImageResult=CSharpGameEngine.Import.Images.ImageOutputCompiler.Execute(Image);
                    WriteResultAtomically(ResultPath,JsonSerializer.SerializeToUtf8Bytes(ImageResult,JsonOptions));return(0,string.Empty);
                }
                if(Envelope.RootElement.TryGetProperty("FileMutationCommand",out _)){
                    var Mutation=JsonSerializer.Deserialize<AssetFileMutationRequest>(RequestBytes,JsonOptions);
                    var MutationResult=AssetFileMutationService.Execute(Mutation!);
                    WriteResultAtomically(ResultPath,JsonSerializer.SerializeToUtf8Bytes(MutationResult,JsonOptions));
                    return(0,string.Empty);
                }
                if(Envelope.RootElement.TryGetProperty("WorkspaceCommand",out _)){
                    var Inventory=JsonSerializer.Deserialize<AssetWorkspaceRequest>(RequestBytes,JsonOptions);
                    var Projection=Inventory?.WorkspaceCommand==2?PreviewFont(Inventory):Inventory?.WorkspaceCommand==3?PreviewAudio(Inventory):AssetWorkspaceService.Inspect(Inventory!);
                    WriteResultAtomically(ResultPath,JsonSerializer.SerializeToUtf8Bytes(Projection,JsonOptions));
                    return(0,string.Empty);
                }
            }
            AssetAuthoringRequest?Request=JsonSerializer.Deserialize<AssetAuthoringRequest>(
                RequestBytes,JsonOptions);
            if(Request is null){ return(1,"Asset authoring request is empty."); }

            AssetAuthoringResult Result=AssetAuthoringService.Execute(Request);
            WriteResultAtomically(ResultPath,JsonSerializer.SerializeToUtf8Bytes(Result,JsonOptions));
            return(0,string.Empty);
        } catch(Exception Ex)when(Ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException){
            return(1,Ex.Message);
        }
    }
    internal static AssetWorkspaceResult PreviewFont(AssetWorkspaceRequest Request){
        try {
            if(Request.WorkspaceCommand!=2||Request.ProtocolVersion!=1||Request.RequestId==Guid.Empty||Request.Sources is not {Length:1}
                ||Request.Sources[0] is not {SourcePath:{Length:>0}Source}||!Path.IsPathFullyQualified(Source)
                ||!Path.GetExtension(Source).Equals(".cgefont",StringComparison.OrdinalIgnoreCase)){
                throw new InvalidDataException("ASSET_FONT_PREVIEW_REQUEST_INVALID");
            }
            var Information=new FileInfo(Source);
            if(Information.Length is <1 or >70*1024*1024){ throw new InvalidDataException("ASSET_FONT_PREVIEW_SIZE_INVALID"); }
            var Font=CSharpGameEngine.Rendering.FontAtlas.Parse(File.ReadAllBytes(Source));
            var Width=Math.Min(512,Font.AtlasWidth);var Height=Math.Min(512,Font.AtlasHeight);
            var Distance=new byte[Width*Height];
            for(var Y=0;Y<Height;Y++){
                for(var X=0;X<Width;X++){ Distance[Y*Width+X]=Font.Distance[(Y*Font.AtlasHeight/Height)*Font.AtlasWidth+X*Font.AtlasWidth/Width]; }
            }
            return new(){RequestId=Request.RequestId,Success=true,FontPreview=new(){SourceWidth=Font.AtlasWidth,SourceHeight=Font.AtlasHeight,GlyphCount=Font.Glyphs.Count,Width=Width,Height=Height,Distance=Distance}};
        }catch(Exception Error)when(Error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidDataException){
            return new(){RequestId=Request.RequestId,Diagnostic=Error.Message};
        }
    }

    /// <summary>.wav の形式と波形ピークを返す。ゲームが実際に読む PCM 8/16/24 ビットと 32 ビット浮動小数点だけを受け付け、読めない形式は診断として返す。</summary>
    internal static AssetWorkspaceResult PreviewAudio(AssetWorkspaceRequest Request){
        try {
            if(Request.WorkspaceCommand!=3||Request.ProtocolVersion!=1||Request.RequestId==Guid.Empty||Request.Sources is not {Length:1}
                ||Request.Sources[0] is not {SourcePath:{Length:>0}Source}||!Path.IsPathFullyQualified(Source)
                ||!Path.GetExtension(Source).Equals(".wav",StringComparison.OrdinalIgnoreCase)){
                throw new InvalidDataException("ASSET_AUDIO_PREVIEW_REQUEST_INVALID");
            }
            var Information=new FileInfo(Source);
            if(Information.Length is <45 or >MaximumAudioPreviewBytes){ throw new InvalidDataException("ASSET_AUDIO_PREVIEW_SIZE_INVALID"); }
            var Bytes=File.ReadAllBytes(Source);
            if(!CSharpGameEngine.Audio.WavReader.TryRead(Bytes,out var Clip,out var Reason)||Clip is null){ throw new InvalidDataException("ASSET_AUDIO_PREVIEW_UNSUPPORTED: "+Reason); }
            return new(){RequestId=Request.RequestId,Success=true,AudioPreview=new(){
                SampleRate=Clip.SampleRate,Channels=Clip.Channels,BitsPerSample=ReadBitsPerSample(Bytes),FrameCount=Clip.FrameCount,
                DurationSeconds=Clip.DurationSeconds,Peaks=ComputePeaks(Clip.Samples,Clip.Channels,AssetAudioPreview.MaximumPeaks)}};
        }catch(Exception Error)when(Error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidDataException){
            return new(){RequestId=Request.RequestId,Diagnostic=Error.Message};
        }
    }
    /// <summary>区間ごとの最大絶対振幅（全チャンネル）を 0..1 に正規化する。区間数はフレーム数を超えない。</summary>
    internal static float[]ComputePeaks(ReadOnlySpan<short>Samples,int Channels,int MaximumBuckets){
        var Frames=Samples.Length/Math.Max(1,Channels);
        var Buckets=Math.Min(MaximumBuckets,Frames);
        var Peaks=new float[Buckets];
        for(var Bucket=0;Bucket<Buckets;Bucket++){
            var First=(int)((long)Bucket*Frames/Buckets);
            var Last=(int)((long)(Bucket+1)*Frames/Buckets);
            var Peak=0;
            for(var Frame=First;Frame<Last;Frame++){
                for(var Channel=0;Channel<Channels;Channel++){ Peak=Math.Max(Peak,Math.Abs((int)Samples[Frame*Channels+Channel])); }
            }
            Peaks[Bucket]=Math.Min(1f,Peak/32768f);
        }
        return Peaks;
    }
    private static int ReadBitsPerSample(byte[]Data){
        var Offset=12;
        while(Offset+8<=Data.Length){
            var Size=BitConverter.ToInt32(Data,Offset+4);
            if(Data[Offset]==(byte)'f'&&Data[Offset+1]==(byte)'m'&&Data[Offset+2]==(byte)'t'&&Data[Offset+3]==(byte)' '&&Size>=16&&Offset+8+16<=Data.Length){ return BitConverter.ToUInt16(Data,Offset+8+14); }
            if(Size<0){ break; }
            Offset+=8+Size+(Size&1);
        }
        return 0;
    }
    private const long MaximumAudioPreviewBytes=256L*1024*1024;
    private static byte[]ReadBoundedRequest(string PathValue){
        FileInfo Information=new(PathValue);
        if(Information.Length<1||Information.Length>MaximumRequestBytes){
            throw new InvalidDataException("Asset authoring request length is outside the supported range.");
        }
        byte[]Bytes=File.ReadAllBytes(PathValue);
        if(Bytes.Length!=Information.Length){
            throw new IOException("Asset authoring request changed while it was read.");
        }
        return Bytes;
    }

    private static void WriteResultAtomically(string PathValue,byte[]Bytes){
        string FullPath=Path.GetFullPath(PathValue);
        string DirectoryPath=Path.GetDirectoryName(FullPath)
            ??throw new ArgumentException("Asset authoring result directory is unavailable.");
        Directory.CreateDirectory(DirectoryPath);
        string Temporary=Path.Combine(
            DirectoryPath,"."+Path.GetFileName(FullPath)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            using(FileStream Stream=new(
                Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,
                FileOptions.WriteThrough)){
                Stream.Write(Bytes);
                Stream.Flush(true);
            }
            if(File.Exists(FullPath)){ File.Replace(Temporary,FullPath,null); }
            else { File.Move(Temporary,FullPath); }
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }
}
