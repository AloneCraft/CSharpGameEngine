using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// Vehicleテンプレートの運転の実走検証(ADR-0077)。アクセル(W)を踏み続け、途中で少しだけ右へハンドル(D)を切り、
/// 追従カメラの位置の推移で、前進したこと・曲がったこと・地面から離れていないことを判定する。
/// カメラは車の進む向きへなだらかに回りながら後ろに付くので、その水平移動の向きが車の進む向きに近い。
/// 固定tickは1フレームに上限回数だけ進むので、判定は距離と角度の範囲に限り、正確な位置は比べない。
/// </summary>
public static class VehicleDriveVerification {
    public const int Frames=60;
    public const int ThrottleStartFrame=10;
    public const int ThrottleEndFrame=50;
    public const int SteerStartFrame=24;
    public const int SteerEndFrame=29;
    /// <summary>ハンドルを切る前の直進の区間と、切ったあとの区間(フレーム)。それぞれの水平移動の向きを比べる。</summary>
    public const int StraightFrom=14;
    public const int StraightTo=22;
    public const int TurnedFrom=34;
    public const int TurnedTo=42;
    public const float MinimumTravelMeters=20f;
    /// <summary>ハンドルを切ったあとの進む向きが、切る前からこれだけは変わる(度)。</summary>
    public const float MinimumTurnDegrees=5f;
    /// <summary>カメラの高さの範囲(m)。地面の上の車の後ろの高さは約2.7 m。地面の抜けや空への飛び出しを弾く。</summary>
    public const float MinimumCameraHeight=1f;
    public const float MaximumCameraHeight=6f;
    public static string InputSchedule=>string.Create(CultureInfo.InvariantCulture,$"W@{ThrottleStartFrame}-{ThrottleEndFrame};D@{SteerStartFrame}-{SteerEndFrame}");
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Executable=ThirdPersonCharacterVerification.ExecutablePath(RepositoryRoot,Configuration,"Vehicle");
        if(!File.Exists(Executable)){
            await Error.WriteLineAsync($"The Vehicle template is not built: {Executable}").ConfigureAwait(false);
            return 2;
        }
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","rigid.character-controller","Vehicle",Adapter);
        Directory.CreateDirectory(Evidence);
        var ReportPath=Path.Combine(Evidence,"report.json");
        File.Delete(ReportPath);
        var Start=new ProcessStartInfo(Executable) {
            WorkingDirectory=Path.GetDirectoryName(Executable)!,
            UseShellExecute=false,
            RedirectStandardError=true,
            RedirectStandardOutput=true,
        };
        Start.Environment["CGE_GRAPHICS_ADAPTER"]=Adapter;
        Start.Environment["CGE_AUTOMATION_FRAMES"]=Frames.ToString(CultureInfo.InvariantCulture);
        Start.Environment["CGE_AUTOMATION_INPUT"]=InputSchedule;
        Start.Environment["CGE_AUTOMATION_REPORT"]=ReportPath;
        Start.Environment["CGE_AUTOMATION_CAPTURE"]=Path.Combine(Evidence,"final.png");
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start the Vehicle template.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Diagnostics=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(await Standard.ConfigureAwait(false)).ConfigureAwait(false);
        await Error.WriteAsync(await Diagnostics.ConfigureAwait(false)).ConfigureAwait(false);
        if(Process.ExitCode!=0||!File.Exists(ReportPath)){
            await Error.WriteLineAsync($"The Vehicle template exited with {Process.ExitCode} and report present={File.Exists(ReportPath)}.").ConfigureAwait(false);
            return 1;
        }
        using var Report=JsonDocument.Parse(await File.ReadAllBytesAsync(ReportPath,CancellationToken).ConfigureAwait(false));
        var Failures=Evaluate(Report.RootElement,out var Summary);
        await Output.WriteLineAsync("CGE-VEHICLE-DRIVE "+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        return Failures.Count==0?0:1;
    }
    /// <summary>レポートを判定し、失敗理由を返す(空なら合格)。<paramref name="Summary"/>は観測値の要約。</summary>
    public static IReadOnlyList<string>Evaluate(JsonElement Report,out string Summary){
        List<string>Failures=[];
        Summary=string.Empty;
        if(!Report.TryGetProperty("schema",out var Schema)||Schema.GetString()!="cge-automation-report/1"){
            Failures.Add("The automation report schema is not cge-automation-report/1.");
            return Failures;
        }
        var Errors=Report.GetProperty("d3d12Errors").GetInt32();
        if(Errors!=0){
            Failures.Add($"The D3D12 debug layer reported {Errors} error(s).");
        }
        var Positions=Report.GetProperty("cameraPositions").EnumerateArray().Select(static Item=>Item.ValueKind==JsonValueKind.Array?(float[]?)[.. Item.EnumerateArray().Select(static Value=>Value.GetSingle())]:null).ToArray();
        if(Positions.Length<Frames||Positions.Skip(ThrottleStartFrame-1).Any(static Position=>Position is null)){
            Failures.Add($"Expected {Frames} frames with a camera pose from frame {ThrottleStartFrame-1}, found {Positions.Length}.");
            return Failures;
        }
        var Travel=Planar(Positions[ThrottleStartFrame-1]!,Positions[Frames-1]!);
        var Straight=Heading(Positions[StraightFrom]!,Positions[StraightTo]!);
        var Turned=Heading(Positions[TurnedFrom]!,Positions[TurnedTo]!);
        var Turn=Straight is { } Before&&Turned is { } After?AngleBetween(Before,After):float.NaN;
        var Lowest=Positions.Skip(ThrottleStartFrame-1).Min(static Position=>Position![1]);
        var Highest=Positions.Skip(ThrottleStartFrame-1).Max(static Position=>Position![1]);
        Summary=string.Create(CultureInfo.InvariantCulture,$"travel={Travel:F3} turn={Turn:F1} lowest={Lowest:F3} highest={Highest:F3} d3d12Errors={Errors}");
        if(Travel<MinimumTravelMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Holding W moved the camera {Travel:F3} m horizontally; expected at least {MinimumTravelMeters} m."));
        }
        if(float.IsNaN(Turn)){
            Failures.Add("The car did not move in both windows that are compared for the turn, so its heading is unknown.");
        }
        else if(Turn<MinimumTurnDegrees){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Steering right turned the direction of travel by {Turn:F1} degrees; expected at least {MinimumTurnDegrees}."));
        }
        if(Lowest<MinimumCameraHeight||Highest>MaximumCameraHeight){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera height ranged from {Lowest:F3} to {Highest:F3} m; expected {MinimumCameraHeight} to {MaximumCameraHeight} m (the car should stay on the ground)."));
        }
        return Failures;
    }
    private static float Planar(float[]From,float[]To)=>MathF.Sqrt(((To[0]-From[0])*(To[0]-From[0]))+((To[2]-From[2])*(To[2]-From[2])));
    /// <summary>水平な移動の向き(単位ベクトル)。ほとんど動いていなければ null。</summary>
    private static (float R,float F)?Heading(float[]From,float[]To){
        var R=To[0]-From[0];
        var F=To[2]-From[2];
        var Length=MathF.Sqrt((R*R)+(F*F));
        return Length<1f?null:(R/Length,F/Length);
    }
    private static float AngleBetween((float R,float F)Left,(float R,float F)Right)=>
        MathF.Acos(Math.Clamp((Left.R*Right.R)+(Left.F*Right.F),-1f,1f))*180f/MathF.PI;
}
