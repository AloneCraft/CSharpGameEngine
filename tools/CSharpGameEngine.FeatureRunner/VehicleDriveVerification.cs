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
    /// <summary>
    /// ハンドルを切る前の直進の区間と、切ったあとの区間(フレーム)。それぞれの水平移動の向きを比べる。
    /// M0109: 切ったあとの区間はハンドルを戻した直後(30〜34)。Chaos 相当の車は UE の値で速く(0-100 km/h 約 3 秒)、WARP では 1 フレームに数 tick 進むので、
    /// 右へ曲がった車は 37 フレームごろにレベルの壁(PlayerStart から +R の壁まで約 18 m)に当たる。以前の区間(34〜42)は壁の跳ね返りを測っていた。
    /// </summary>
    public const int StraightFrom=14;
    public const int StraightTo=22;
    public const int TurnedFrom=30;
    public const int TurnedTo=34;
    public const float MinimumTravelMeters=20f;
    /// <summary>ハンドルを切ったあとの進む向きが、切る前からこれだけは変わる(度)。</summary>
    public const float MinimumTurnDegrees=5f;
    /// <summary>
    /// カメラの高さの範囲(m)。地面の抜けや空への飛び出しを弾く。M0112: レベルが UE の Landscape になったので、根拠を測り直した。路面(高さ 0.08 m)の上の追従カメラは約 1.5〜1.65 m
    /// (WARP の実走で最低 1.534、最高 1.645)。中央の丘(最大 2.84 m)の上まで登っても 4.5 m 以下で、地面を抜ければ 1 m を割る(M0112 で縁石を衝突に含めたときは -47 m まで落ちた)。
    /// M0124: 縁石が衝突するようになり、縁石を越える揺れで 1.498〜1.717 m(WARP)。範囲は変えない。
    /// </summary>
    public const float MinimumCameraHeight=1f;
    public const float MaximumCameraHeight=6f;
    /// <summary>走り出したあとのHUD(速度とギアの文字)の画面レイヤーが、これだけ違う状態を取る。速度の数字が変わるので、止まったままの画面では満たせない。</summary>
    public const int MinimumHudStates=3;
    /// <summary>半分のトリガーと半分のスティックで走ったときの最低の進み(m)。動かない(値が 0 に落ちる)ことを弾く。</summary>
    public const float MinimumAnalogTravelMeters=3f;
    /// <summary>半分のアクセルが、キーで踏み切ったときの何倍までしか進まないか。アナログ値がオン・オフにされていれば 1 倍になる。</summary>
    public const float MaximumAnalogTravelRatio=0.9f;
    public const float MinimumAnalogTurnDegrees=0.5f;
    /// <summary>キーの実走と同じ区間・同じ時間割りの、ゲームパッドの実走(M0108)。右トリガーを半分倒し、ハンドルはキーで切る。</summary>
    public static string AnalogThrottleSchedule=>string.Create(CultureInfo.InvariantCulture,$"GamepadRightTrigger=0.5@{ThrottleStartFrame}-{ThrottleEndFrame};D@{SteerStartFrame}-{SteerEndFrame}");
    /// <summary>左スティックの横を半分倒し、アクセルはキーで踏む。</summary>
    public static string AnalogSteeringSchedule=>string.Create(CultureInfo.InvariantCulture,$"W@{ThrottleStartFrame}-{ThrottleEndFrame};GamepadLeftX=0.5@{SteerStartFrame}-{SteerEndFrame}");
    public static string InputSchedule=>string.Create(CultureInfo.InvariantCulture,$"W@{ThrottleStartFrame}-{ThrottleEndFrame};D@{SteerStartFrame}-{SteerEndFrame}");
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Executable=ThirdPersonCharacterVerification.ExecutablePath(RepositoryRoot,Configuration,"Vehicle");
        if(!File.Exists(Executable)){
            await Error.WriteLineAsync($"The Vehicle template is not built: {Executable}").ConfigureAwait(false);
            return 2;
        }
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","rigid.character-controller","Vehicle",Adapter);
        return await RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false);
    }
    /// <summary>Drives a built Vehicle game (the template here, or a project generated from the package) and judges its report.</summary>
    public static async Task<int>RunGameAsync(string Executable,string Evidence,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Digital=await RunOnceAsync(Executable,Evidence,Adapter,InputSchedule,"CGE-VEHICLE-DRIVE ",Output,Error,CancellationToken).ConfigureAwait(false);
        if(Digital.ExitCode!=0){ return Digital.ExitCode; }
        // M0108: the same drive on a gamepad, once with half a trigger (the keys steer) and once with half a stick (the key accelerates). The analog values must reach the car as they are, not as on/off.
        var Throttle=await RunOnceAsync(Executable,Path.Combine(Evidence,"analog-throttle"),Adapter,AnalogThrottleSchedule,"CGE-VEHICLE-DRIVE-ANALOG-THROTTLE ",Output,Error,CancellationToken).ConfigureAwait(false);
        if(Throttle.ExitCode!=0){ return Throttle.ExitCode; }
        var Steering=await RunOnceAsync(Executable,Path.Combine(Evidence,"analog-steering"),Adapter,AnalogSteeringSchedule,"CGE-VEHICLE-DRIVE-ANALOG-STEERING ",Output,Error,CancellationToken).ConfigureAwait(false);
        if(Steering.ExitCode!=0){ return Steering.ExitCode; }
        var Failures=EvaluateAnalog(Digital.Measure,Throttle.Measure,Steering.Measure,out var Summary);
        await Output.WriteLineAsync("CGE-VEHICLE-DRIVE-ANALOG-COMPARED "+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        return Failures.Count==0?0:1;
    }
    /// <summary>How far and how much a drive moved the camera, for comparing two drives.</summary>
    public readonly record struct Measurement(float Travel,float Turn);
    /// <summary>
    /// 半分のトリガー(ハンドルはキー)で走ったときが、キーで踏み切ったときより短くしか進まず(<see cref="MaximumAnalogTravelRatio"/>倍まで)、
    /// 半分のスティック(アクセルはキー)で走ったときが、キーで切り切ったときより小さくしか曲がらないこと(どちらも動くこと)を判定する。
    /// アナログの値がオン・オフにされていれば同じになる。
    /// </summary>
    public static IReadOnlyList<string>EvaluateAnalog(Measurement Digital,Measurement HalfTrigger,Measurement HalfStick,out string Summary){
        List<string>Failures=[];
        Summary=string.Create(CultureInfo.InvariantCulture,$"keysTravel={Digital.Travel:F3} halfTriggerTravel={HalfTrigger.Travel:F3} keysTurn={Digital.Turn:F1} halfStickTurn={HalfStick.Turn:F1}");
        if(!(HalfTrigger.Travel>=MinimumAnalogTravelMeters)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Half a trigger moved the camera {HalfTrigger.Travel:F3} m; expected at least {MinimumAnalogTravelMeters} m."));
        }
        if(!(HalfTrigger.Travel<=Digital.Travel*MaximumAnalogTravelRatio)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Half a trigger moved the camera {HalfTrigger.Travel:F3} m against {Digital.Travel:F3} m for a full key press; expected at most {MaximumAnalogTravelRatio} times as far (the analog value must scale the throttle)."));
        }
        if(!(HalfStick.Turn>=MinimumAnalogTurnDegrees)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Half a stick turned the direction of travel by {HalfStick.Turn:F1} degrees; expected at least {MinimumAnalogTurnDegrees}."));
        }
        if(!(HalfStick.Turn<Digital.Turn)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Half a stick turned the car {HalfStick.Turn:F1} degrees against {Digital.Turn:F1} for a full key press; expected less (the analog value must scale the steering)."));
        }
        return Failures;
    }
    private static async Task<(int ExitCode,Measurement Measure)>RunOnceAsync(string Executable,string Evidence,string Adapter,string Schedule,string Label,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
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
        Start.Environment["CGE_D3D12_DEBUG_LAYER"]="1";
        Start.Environment["CGE_AUTOMATION_FRAMES"]=Frames.ToString(CultureInfo.InvariantCulture);
        Start.Environment["CGE_AUTOMATION_INPUT"]=Schedule;
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
            return (1,default);
        }
        using var Report=JsonDocument.Parse(await File.ReadAllBytesAsync(ReportPath,CancellationToken).ConfigureAwait(false));
        var Failures=Evaluate(Report.RootElement,out var Summary,Schedule!=InputSchedule);
        await Output.WriteLineAsync(Label+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        return (Failures.Count==0?0:1,Measure(Report.RootElement));
    }
    /// <summary>レポートを判定し、失敗理由を返す(空なら合格)。<paramref name="Summary"/>は観測値の要約。</summary>
    public static IReadOnlyList<string>Evaluate(JsonElement Report,out string Summary,bool AnalogDrive=false){
        List<string>Failures=[];
        Summary=string.Empty;
        if(!Report.TryGetProperty("schema",out var Schema)||Schema.GetString()!="cge-automation-report/1"){
            Failures.Add("The automation report schema is not cge-automation-report/1.");
            return Failures;
        }
        if(!Report.TryGetProperty("d3d12DebugLayerEnabled",out var DebugLayer)||DebugLayer.ValueKind!=JsonValueKind.True){
            Failures.Add("The D3D12 debug layer was not enabled during template verification.");
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
        var HudStates=DistinctHudStates(Report,out var HudRecorded);
        var Lowest=Positions.Skip(ThrottleStartFrame-1).Min(static Position=>Position![1]);
        var Highest=Positions.Skip(ThrottleStartFrame-1).Max(static Position=>Position![1]);
        Summary=string.Create(CultureInfo.InvariantCulture,$"travel={Travel:F3} turn={Turn:F1} lowest={Lowest:F3} highest={Highest:F3} hudStates={HudStates} d3d12Errors={Errors}");
        if(!AnalogDrive&&Travel<MinimumTravelMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Holding W moved the camera {Travel:F3} m horizontally; expected at least {MinimumTravelMeters} m."));
        }
        // The analog drive is judged against the key drive (EvaluateAnalog), not against the key drive's minimums.
        if(!AnalogDrive&&float.IsNaN(Turn)){
            Failures.Add("The car did not move in both windows that are compared for the turn, so its heading is unknown.");
        }
        else if(!AnalogDrive&&Turn<MinimumTurnDegrees){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Steering right turned the direction of travel by {Turn:F1} degrees; expected at least {MinimumTurnDegrees}."));
        }
        if(Lowest<MinimumCameraHeight||Highest>MaximumCameraHeight){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera height ranged from {Lowest:F3} to {Highest:F3} m; expected {MinimumCameraHeight} to {MaximumCameraHeight} m (the car should stay on the ground)."));
        }
        if(!HudRecorded){
            Failures.Add("The report has no screenLayerFingerprints; the HUD cannot be judged.");
        }
        else if(HudStates<MinimumHudStates){
            Failures.Add($"The HUD took {HudStates} distinct state(s) after the car started; expected at least {MinimumHudStates} (the speed readout should change while driving).");
        }
        return Failures;
    }
    /// <summary>How far the camera travelled and by how many degrees its direction of travel turned (0 when the car did not move in a compared window).</summary>
    public static Measurement Measure(JsonElement Report){
        var Positions=Report.GetProperty("cameraPositions").EnumerateArray().Select(static Item=>Item.ValueKind==JsonValueKind.Array?(float[]?)[.. Item.EnumerateArray().Select(static Value=>Value.GetSingle())]:null).ToArray();
        if(Positions.Length<Frames||Positions.Skip(ThrottleStartFrame-1).Any(static Position=>Position is null)){ return default; }
        var Straight=Heading(Positions[StraightFrom]!,Positions[StraightTo]!);
        var Turned=Heading(Positions[TurnedFrom]!,Positions[TurnedTo]!);
        return new Measurement(Planar(Positions[ThrottleStartFrame-1]!,Positions[Frames-1]!),Straight is { } Before&&Turned is { } After?AngleBetween(Before,After):0f);
    }
    /// <summary>走り出したあとのフレームで、画面レイヤーの指紋(0=何も描かない)が取った、0でない値の種類の数。</summary>
    private static int DistinctHudStates(JsonElement Report,out bool Recorded){
        Recorded=Report.TryGetProperty("screenLayerFingerprints",out var Fingerprints)&&Fingerprints.ValueKind==JsonValueKind.Array;
        if(!Recorded){ return 0; }
        return Fingerprints.EnumerateArray().Skip(ThrottleStartFrame-1).Select(static Item=>Item.GetString()).Where(static Text=>!string.IsNullOrEmpty(Text)&&Text!="0000000000000000").Distinct(StringComparer.Ordinal).Count();
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
