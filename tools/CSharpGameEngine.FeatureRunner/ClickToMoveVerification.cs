using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// TopDownテンプレートのクリック移動の実走検証(ADR-0077)。画面上部の1点を数フレームだけクリックし、その地点まで歩いて止まることを
/// カメラ位置の推移で判定する。カメラは固定の角度でキャラクターに追従するので、その水平移動がキャラクターの歩行になる。
/// 固定tickは1フレームに上限回数だけ進むので、判定は距離の範囲と停止の有無に限る。
/// </summary>
public static class ClickToMoveVerification {
    public const int Frames=80;
    public const int ClickStartFrame=20;
    public const int ClickEndFrame=25;
    /// <summary>カーソルの位置(クライアント領域の割合)。画面の中央上寄りで、キャラクターの前方の地面を指す。</summary>
    public const string Cursor="0.5,0.1";
    /// <summary>クリックを離したあとも目的地へ歩き続けて、これだけは動く(m)。</summary>
    public const float MinimumWalkMeters=2f;
    /// <summary>カーソルを追い続けた場合のように行き過ぎていない上限(m)。目的地は数mの先。</summary>
    public const float MaximumWalkMeters=12f;
    /// <summary>最後の10フレームの動きがこの距離(m)未満なら止まったとみなす。</summary>
    public const float MaximumFinalDriftMeters=0.1f;
    public const int SettledFrames=10;
    public static string InputSchedule=>string.Create(CultureInfo.InvariantCulture,$"MouseLeft@{ClickStartFrame}-{ClickEndFrame}");
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Executable=ThirdPersonCharacterVerification.ExecutablePath(RepositoryRoot,Configuration,"TopDown");
        if(!File.Exists(Executable)){
            await Error.WriteLineAsync($"The TopDown template is not built: {Executable}").ConfigureAwait(false);
            return 2;
        }
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","rigid.character-controller","TopDown",Adapter);
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
        Start.Environment["CGE_AUTOMATION_CURSOR"]=Cursor;
        Start.Environment["CGE_AUTOMATION_REPORT"]=ReportPath;
        Start.Environment["CGE_AUTOMATION_CAPTURE"]=Path.Combine(Evidence,"final.png");
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start the TopDown template.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Diagnostics=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(await Standard.ConfigureAwait(false)).ConfigureAwait(false);
        await Error.WriteAsync(await Diagnostics.ConfigureAwait(false)).ConfigureAwait(false);
        if(Process.ExitCode!=0||!File.Exists(ReportPath)){
            await Error.WriteLineAsync($"The TopDown template exited with {Process.ExitCode} and report present={File.Exists(ReportPath)}.").ConfigureAwait(false);
            return 1;
        }
        using var Report=JsonDocument.Parse(await File.ReadAllBytesAsync(ReportPath,CancellationToken).ConfigureAwait(false));
        var Failures=Evaluate(Report.RootElement,out var Summary);
        await Output.WriteLineAsync("CGE-TOPDOWN-CLICK "+Summary).ConfigureAwait(false);
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
        if(Positions.Length<Frames||Positions.Skip(ClickStartFrame-1).Any(static Position=>Position is null)){
            Failures.Add($"Expected {Frames} frames with a camera pose from frame {ClickStartFrame-1}, found {Positions.Length}.");
            return Failures;
        }
        var Walk=Planar(Positions[ClickStartFrame-1]!,Positions[Frames-1]!);
        var Drift=Planar(Positions[Frames-SettledFrames-1]!,Positions[Frames-1]!);
        var Audio=Report.TryGetProperty("audioRequests",out var AudioElement)?AudioElement.GetInt32():-1;
        var AudioSuffix=Audio<0?string.Empty:string.Create(CultureInfo.InvariantCulture,$" audioRequests={Audio}");
        Summary=string.Create(CultureInfo.InvariantCulture,$"walk={Walk:F3} finalDrift={Drift:F3} d3d12Errors={Errors}{AudioSuffix}");
        if(Walk<MinimumWalkMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Clicking the ground moved the camera {Walk:F3} m horizontally; expected at least {MinimumWalkMeters} m."));
        }
        if(Walk>MaximumWalkMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera moved {Walk:F3} m horizontally; expected at most {MaximumWalkMeters} m (the destination is a few metres away)."));
        }
        if(Drift>=MaximumFinalDriftMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera still moved {Drift:F3} m in the last {SettledFrames} frames; the character did not stop at the destination."));
        }
        if(Audio==0){
            Failures.Add("Walking to the destination asked for no footstep sound.");
        }
        return Failures;
    }
    private static float Planar(float[]From,float[]To)=>MathF.Sqrt(((To[0]-From[0])*(To[0]-From[0]))+((To[2]-From[2])*(To[2]-From[2])));
}
