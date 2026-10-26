using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Simulation;
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
    /// <summary>
    /// <c>Code[=Value]@Start-End</c>を<c>;</c>で区切った押下予定。StartからEnd直前までの表示フレームの間、押下し続ける。
    /// <c>Value</c>は、ゲームパッドの軸・トリガー(<c>GamepadRightTrigger=0.5@10-40</c>)の値と、マウスの移動量(<c>MouseDeltaX=12@5-9</c>。範囲の各フレームのカウント)に書く(M0108)。
    /// </summary>
    public const string InputVariable="CGE_AUTOMATION_INPUT";
    public const string ReportVariable="CGE_AUTOMATION_REPORT";
    /// <summary>スクリプトで指定するカーソル位置。クライアント領域に対する<c>x,y</c>の割合（0～1、左上を原点）。これを使う実行では実カーソルを読み取らない。</summary>
    public const string CursorVariable="CGE_AUTOMATION_CURSOR";
    /// <summary>
    /// デバッグビュー：<c>r,u,f,yawDegrees,pitchDegrees</c>。シーンカメラを置き換え、カメラがぶら下がるEntityからのワールド空間オフセット位置に配置する。Yaw（U軸
    /// まわり）とPitch（R軸まわり）で回転させるため、車両の横から実行を撮影できる（M0109）。
    /// </summary>
    public const string CameraVariable="CGE_AUTOMATION_CAMERA";
    public const string ReportSchema="cge-automation-report/1";
    private readonly List<Vector3?>CameraPositions=[];
    /// <summary>表示フレームごとに1件。カメラがぶら下がるEntity（スプリングアームが追従するキャラクター）の位置、またはnull（M0107）。</summary>
    private readonly List<Vector3?>FocusPositions=[];
    /// <summary>表示フレームごとに1件。スクリーンレイヤーの頂点のフィンガープリント（ADR-0078/M0104）。頂点がないフレームでは0。</summary>
    private readonly List<ulong>ScreenLayerFingerprints=[];
    /// <summary>表示フレームごとに1件。レンダラーが描画したオブジェクト数（ADR-0078/M0105）。効果の生成時に増え、終了時に減ることを確認できる。</summary>
    private readonly List<int>RenderObjectCounts=[];
    /// <summary>ADR-0078/M0110：直近フレームで距離場を持つメッシュ数と、グローバル距離場内のオブジェクト数。</summary>
    private int DistanceFieldMeshes;
    private int DistanceFieldObjects;
    /// <summary>ADR-0078/M0111：直近フレームがLumen風GIで照明されたかどうかと、該当フレーム数。</summary>
    private bool LumenActive;
    private CSharpGameEngine.Rendering.VirtualShadowStatistics VirtualShadows;
    private float CascadedShadowMilliseconds,VirtualShadowMilliseconds;
    private CSharpGameEngine.Rendering.DirectionalShadowSettings ShadowSettings;
    private string GraphicsAdapter=string.Empty;
    private bool IsWarp;
    private int SurfaceWidth,SurfaceHeight;
    private int LumenFrames;
    /// <summary>表示フレームごとに1件。前フレーム表示からの壁時計時間（ミリ秒。M0110ではWARPのフレーム時間の証拠）。</summary>
    private readonly List<float>FrameMilliseconds=[];
    private readonly System.Diagnostics.Stopwatch FrameClock=new();
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
    /// <summary>
    /// 自動実行の1フレーム分の経過時間。スケジューラーのフレームあたり上限をティック数で表し、
    /// 整数アキュムレーターを端数なしの整数ステップで進める（<see cref="EngineHost.FrameTime"/>）。
    /// </summary>
    internal static (long ElapsedCounter,long CounterFrequency)FixedFrameTime(FixedTickScheduler Scheduler)=>
        (Scheduler.MaximumStepsPerFrame,Scheduler.TickRate);

    /// <summary><see cref="InputVariable"/>を解析する。値が空なら何もスケジュールしない。</summary>
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
                Error=$"'{Entry}' is not Code[=Value]@Start-End with Start < End.";
                return false;
            }
            var Code=Entry[..At];
            var Value=1f;
            var ValueAt=Code.IndexOf('=');
            if(ValueAt>=0){
                // M0108：アナログ入力では、ゲームパッドの軸／トリガー値、またはフレームごとのマウス移動量を指定する。
                if(!float.TryParse(Code.AsSpan(ValueAt+1),NumberStyles.Float,CultureInfo.InvariantCulture,out Value)||!float.IsFinite(Value)){
                    Error=$"'{Entry}' has a value that is not a finite number.";
                    return false;
                }
                Code=Code[..ValueAt];
                if(!IsAnalogCode(Code)){
                    Error=$"'{Entry}': '{Code}' does not take a value; only GamepadLeftX/LeftY/RightX/RightY, GamepadLeftTrigger/RightTrigger, the gamepad buttons and MouseDeltaX/MouseDeltaY do.";
                    return false;
                }
            }
            if(Code.Length==0
                ||Code.StartsWith("Gamepad",StringComparison.Ordinal)&&!Win32InputEventAdapter.IsGamepadInjectionCode(Code)
                ||Code.StartsWith("MouseDelta",StringComparison.Ordinal)&&!IsMouseDeltaCode(Code)){
                Error=$"'{Entry}' names an unknown control '{Code}'.";
                return false;
            }
            Result.Add(new RuntimeAutomationKeyHold(Code,Start,End,Value));
        }
        return true;
    }
    private static bool IsMouseDeltaCode(string Code)=>Code is "MouseDeltaX" or "MouseDeltaY";
    private static bool IsAnalogCode(string Code)=>IsMouseDeltaCode(Code)||Win32InputEventAdapter.IsGamepadInjectionCode(Code);
    /// <summary>
    /// これから表示するフレームが範囲内にある間、スケジュールされた各入力を保持する。<c>MouseLeft</c>は左マウスボタンを押し続け、
    /// <c>MouseDeltaX</c>と<c>MouseDeltaY</c>は範囲内の各フレームで指定値（カウント単位）だけマウスを移動させる。
    /// <c>Gamepad…</c>はコントローラーの軸、トリガー、ボタンを指定値にする（範囲外では0）。それ以外はキーボードキーとして扱う（M0108）。
    /// </summary>
    internal void ApplyInput(Win32Window Window){
        var Frame=this.PresentedFrames;
        var DeltaX=0f;
        var DeltaY=0f;
        foreach(var Code in this.Input.Select(static Hold=>Hold.Code).Distinct(StringComparer.Ordinal)){
            var Active=this.Input.FirstOrDefault(Hold=>Hold.Code==Code&&Hold.Contains(Frame));
            var Held=Active.Code is not null;
            if(Code=="MouseDeltaX"){ if(Held){ DeltaX+=Active.Value; } }
            else if(Code=="MouseDeltaY"){ if(Held){ DeltaY+=Active.Value; } }
            else if(Code.StartsWith("Mouse",StringComparison.Ordinal)){ Window.InjectMouseButton(Code["Mouse".Length..],Held); }
            else if(Code.StartsWith("Gamepad",StringComparison.Ordinal)){ Window.InjectGamepad(Code,Held?Active.Value:0f); }
            else { Window.InjectKey(Code,Held); }
        }
        if(DeltaX!=0f||DeltaY!=0f){ Window.InjectMouseDelta(DeltaX,DeltaY); }
    }
    /// <summary>ゲームがこれまでに要求したサウンド（ADR-0077）と、デバイスが実際に開始した数。検証時にレポートから読み取る。</summary>
    private int AudioRequests;
    private int AudioStarted;
    /// <summary>クライアント領域に対する割合で表したスクリプトカーソル。nullなら実カーソルを使う。</summary>
    internal float[]?CameraOverride { get;}=ParseCameraOverride(Environment.GetEnvironmentVariable(CameraVariable));
    private static float[]?ParseCameraOverride(string?Text){
        var Parts=(Text??string.Empty).Split(',',StringSplitOptions.TrimEntries);
        var Values=new float[5];
        if(Parts.Length!=5){ return null; }
        for(var Index=0;Index<5;Index++){
            if(!float.TryParse(Parts[Index],NumberStyles.Float,CultureInfo.InvariantCulture,out Values[Index])||!float.IsFinite(Values[Index])){ return null; }
        }
        return Values;
    }
    internal (float X,float Y)? ScriptedCursor { get;}=ParseCursor(Environment.GetEnvironmentVariable(CursorVariable));
    private static (float X,float Y)?ParseCursor(string?Text){
        var Parts=(Text??string.Empty).Split(',',StringSplitOptions.TrimEntries);
        return Parts.Length==2&&float.TryParse(Parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var X)&&float.TryParse(Parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var Y)?(X,Y):null;
    }
    internal bool CapturesNextFrame=>this.CapturePath is not null&&this.PresentedFrames+1==this.ExitAfterFrames;
    internal void RecordPresented(RenderServer Renderer,Vector3?CameraPosition,int AudioRequests=0,int AudioStarted=0,ulong ScreenLayerFingerprint=0,Vector3?FocusPosition=null){
        this.AudioRequests=AudioRequests;
        this.AudioStarted=AudioStarted;
        var Captured=this.CapturesNextFrame;
        this.PresentedFrames++;
        this.CameraPositions.Add(CameraPosition);
        this.FocusPositions.Add(FocusPosition);
        this.ScreenLayerFingerprints.Add(ScreenLayerFingerprint);
        this.RenderObjectCounts.Add(Renderer.ActiveObjectCount);
        this.DistanceFieldMeshes=Renderer.DistanceFieldMeshCount;
        this.DistanceFieldObjects=Renderer.DistanceFieldObjectCount;
        this.LumenActive=Renderer.LumenActive;
        this.VirtualShadows=Renderer.VirtualShadowStatistics;
        this.CascadedShadowMilliseconds=Renderer.CascadedShadowGpuMilliseconds;
        this.VirtualShadowMilliseconds=Renderer.VirtualShadowGpuMilliseconds;
        this.ShadowSettings=Renderer.DirectionalShadows;
        this.GraphicsAdapter=Renderer.AdapterDescription;this.IsWarp=Renderer.IsWarp;
        this.SurfaceWidth=Renderer.SurfaceWidth;this.SurfaceHeight=Renderer.SurfaceHeight;
        if(this.LumenActive){
            this.LumenFrames++;
        }
        this.FrameMilliseconds.Add(this.FrameClock.IsRunning?this.FrameClock.ElapsedTicks*(1000f/System.Diagnostics.Stopwatch.Frequency):0f);
        this.FrameClock.Restart();
        if(this.Completed&&this.ReportPath is not null){
            this.WriteReport(Renderer.ErrorOrWorseMessageCount,Renderer.DebugLayerEnabled);
        }
        if(!Captured){
            return;
        }
        var Frame=Renderer.LastCapturedFrame??throw new InvalidOperationException("The automation capture frame was not read back.");
        WritePng(Frame,this.CapturePath!);
        // キャプチャ画像は検証の証拠となるため、
        // その証拠の適格性に関わるデバイス診断も隣に報告する（D3D12デバッグレイヤーのエラーがあれば、妥当に見える画像でも実行失敗とする）。
        foreach(var Message in Renderer.DebugMessages.Where(static Message=>Message.IsErrorOrWorse).Take(20)){
            Console.Error.WriteLine("CGE-AUTOMATION-D3D12 "+Message);
        }
        Console.Error.WriteLine($"CGE-AUTOMATION-CAPTURE path={this.CapturePath} d3d12Errors={Renderer.ErrorOrWorseMessageCount} debugLayerEnabled={Renderer.DebugLayerEnabled}");
    }
    private void WriteReport(int D3D12Errors,bool DebugLayerEnabled){
        var Directory=System.IO.Path.GetDirectoryName(this.ReportPath);
        if(!string.IsNullOrEmpty(Directory)){
            System.IO.Directory.CreateDirectory(Directory);
        }
        using var Output=File.Create(this.ReportPath!);
        using var Writer=new Utf8JsonWriter(Output,new JsonWriterOptions { Indented=true });
        Writer.WriteStartObject();
        Writer.WriteString("schema",ReportSchema);
        Writer.WriteString("graphicsAdapter",this.GraphicsAdapter);
        Writer.WriteBoolean("isWarp",this.IsWarp);
        Writer.WriteNumber("surfaceWidth",this.SurfaceWidth);Writer.WriteNumber("surfaceHeight",this.SurfaceHeight);
        Writer.WriteNumber("frames",this.PresentedFrames);
        Writer.WriteNumber("d3d12Errors",D3D12Errors);
        Writer.WriteBoolean("d3d12DebugLayerEnabled",DebugLayerEnabled);
        Writer.WriteNumber("audioRequests",this.AudioRequests);
        Writer.WriteNumber("audioStarted",this.AudioStarted);
        // カメラの基準座標系で表す、フレームごとのカメラ位置。表示フレームごとに1件。
        WritePositions(Writer,"cameraPositions",this.CameraPositions);
        // カメラの親（キャラクター）の位置。表示フレームごとに1件。カメラに親がなければnull（M0107）。
        WritePositions(Writer,"focusPositions",this.FocusPositions);
        // 16進テキストで保持する。64ビット値はJSON数値では正確に保持できない。
        Writer.WriteStartArray("screenLayerFingerprints");
        foreach(var Fingerprint in this.ScreenLayerFingerprints){
            Writer.WriteStringValue(Fingerprint.ToString("x16",CultureInfo.InvariantCulture));
        }
        Writer.WriteEndArray();
        Writer.WriteStartArray("renderObjectCounts");
        foreach(var Count in this.RenderObjectCounts){
            Writer.WriteNumberValue(Count);
        }
        Writer.WriteEndArray();
        Writer.WriteNumber("distanceFieldMeshes",this.DistanceFieldMeshes);
        Writer.WriteNumber("distanceFieldObjects",this.DistanceFieldObjects);
        Writer.WriteBoolean("lumenActive",this.LumenActive);
        Writer.WriteNumber("lumenFrames",this.LumenFrames);
        Writer.WriteStartObject("virtualShadows");
        Writer.WriteString("mode",this.ShadowSettings.Mode.ToString());
        Writer.WriteBoolean("comparison",this.ShadowSettings.Compare);
        Writer.WriteNumber("cascadedGpuMilliseconds",this.CascadedShadowMilliseconds);
        Writer.WriteNumber("virtualPagesGpuMilliseconds",this.VirtualShadowMilliseconds);
        Writer.WriteBoolean("softShadows",this.ShadowSettings.SoftShadows);
        Writer.WriteNumber("sourceAngularDiameterDegrees",this.ShadowSettings.SourceAngularDiameterDegrees);
        Writer.WriteNumber("maximumSoftShadowRadiusMeters",this.ShadowSettings.MaximumSoftShadowRadiusMeters);
        Writer.WriteNumber("requested",this.VirtualShadows.Requested);
        Writer.WriteNumber("resident",this.VirtualShadows.Resident);
        Writer.WriteNumber("rendered",this.VirtualShadows.Rendered);
        Writer.WriteNumber("reused",this.VirtualShadows.Reused);
        Writer.WriteNumber("missingRequests",this.VirtualShadows.Missing);
        Writer.WriteNumber("invalidated",this.VirtualShadows.Invalidated);
        Writer.WriteEndObject();
        Writer.WriteStartArray("frameMilliseconds");
        foreach(var Milliseconds in this.FrameMilliseconds){
            Writer.WriteNumberValue(MathF.Round(Milliseconds,2));
        }
        Writer.WriteEndArray();
        Writer.WriteEndObject();
    }
    private static void WritePositions(Utf8JsonWriter Writer,string Name,List<Vector3?>Positions){
        Writer.WriteStartArray(Name);
        foreach(var Position in Positions){
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
    }
    /// <summary>R8G8B8A8のReadback結果を、行ピッチを除いた不透明RGBのPNGとして保存する。</summary>
    public static void WritePng(PixelReadbackResult Frame,string Path){
        if(Frame.Format!=RenderPixelFormat.Rgba8Unorm){
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
/// <summary>
/// スケジュールされた入力1件。<see cref="StartFrame"/>から<see cref="EndFrame"/>の直前まで押下状態にする。
/// <see cref="Value"/>はキー／ボタンでは1、アナログ入力（ゲームパッドの軸／トリガー、またはフレームごとのマウス移動カウント）ではその値を表す。
/// </summary>
public readonly record struct RuntimeAutomationKeyHold(string Code,int StartFrame,int EndFrame,float Value=1f) {
    public bool Contains(int Frame)=>Frame>=this.StartFrame&&Frame<this.EndFrame;
}
