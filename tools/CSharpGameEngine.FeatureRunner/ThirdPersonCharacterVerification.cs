using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// rigid.character-controllerのテンプレート実走検証(ADR-0067)。ThirdPersonテンプレートの実行ファイルを
/// 自動検証フック付きで起動し、フレーム指定のキー入力(W押下→Space)に対するカメラ位置の推移を判定する。
/// カメラはSpringArmでキャラクターに追従するため、その位置でキャラクターの移動とジャンプを観測する。
/// 固定tickは壁時計で進むので、判定は距離の下限と着地の有無に限り、正確な位置は比べない。
/// </summary>
public static class ThirdPersonCharacterVerification {
    public const int Frames=110;
    public const int WalkStartFrame=20;
    public const int WalkEndFrame=40;
    public const int JumpFrame=50;
    public const float MinimumWalkMeters=1f;
    public const float MinimumJumpMeters=0.5f;
    public const float MinimumDescentMeters=0.3f;
    /// <summary>床(高さ0)の上に立つカメラの高さは約1 m。これを大きく下回るのは床の抜けとみなす。</summary>
    public const float MinimumCameraHeight=0.5f;
    public static string InputSchedule=>string.Create(CultureInfo.InvariantCulture,$"W@{WalkStartFrame}-{WalkEndFrame};SpaceBar@{JumpFrame}-{JumpFrame+1}");
    public static string ExecutablePath(string RepositoryRoot,string Configuration)=>Path.Combine(
        RepositoryRoot,"templates","CSharpGameEngine.Templates.ThirdPerson","bin",Configuration,"net10.0-windows10.0.19041.0","win-x64","CgeThirdPerson.exe");
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Executable=ExecutablePath(RepositoryRoot,Configuration);
        if(!File.Exists(Executable)){
            await Error.WriteLineAsync($"The ThirdPerson template is not built: {Executable}").ConfigureAwait(false);
            return 2;
        }
        return await RunGameAsync(Executable,Path.Combine(RepositoryRoot,".artifacts","feature-evidence","rigid.character-controller",Adapter),Adapter,Output,Error,CancellationToken).ConfigureAwait(false);
    }
    /// <summary>Runs a built ThirdPerson game with the scripted input and judges its report (<see cref="Evaluate"/>).</summary>
    public static async Task<int>RunGameAsync(string Executable,string Evidence,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
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
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start the ThirdPerson template.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Diagnostics=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(await Standard.ConfigureAwait(false)).ConfigureAwait(false);
        await Error.WriteAsync(await Diagnostics.ConfigureAwait(false)).ConfigureAwait(false);
        if(Process.ExitCode!=0||!File.Exists(ReportPath)){
            await Error.WriteLineAsync($"The ThirdPerson template exited with {Process.ExitCode} and report present={File.Exists(ReportPath)}.").ConfigureAwait(false);
            return 1;
        }
        using var Report=JsonDocument.Parse(await File.ReadAllBytesAsync(ReportPath,CancellationToken).ConfigureAwait(false));
        var Failures=Evaluate(Report.RootElement,out var Summary);
        await Output.WriteLineAsync("CGE-THIRDPERSON-CHARACTER "+Summary).ConfigureAwait(false);
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
        if(Positions.Length<Frames||Positions.Skip(WalkStartFrame-1).Any(static Position=>Position is null)){
            Failures.Add($"Expected {Frames} frames with a camera pose from frame {WalkStartFrame-1}, found {Positions.Length}.");
            return Failures;
        }
        var BeforeWalk=Positions[WalkStartFrame-1]!;
        var AfterWalk=Positions[JumpFrame-1]!;
        // The camera follows the character, so its horizontal travel is the character's walk in any facing.
        var Walk=MathF.Sqrt(((AfterWalk[0]-BeforeWalk[0])*(AfterWalk[0]-BeforeWalk[0]))+((AfterWalk[2]-BeforeWalk[2])*(AfterWalk[2]-BeforeWalk[2])));
        var Apex=Positions.Skip(JumpFrame).Max(static Position=>Position![1]);
        var Rise=Apex-AfterWalk[1];
        var Descent=Apex-Positions[^1]![1];
        var Lowest=Positions.Skip(WalkStartFrame-1).Min(static Position=>Position![1]);
        Summary=string.Create(CultureInfo.InvariantCulture,$"walk={Walk:F3} rise={Rise:F3} descent={Descent:F3} lowest={Lowest:F3} d3d12Errors={Errors}");
        if(Walk<MinimumWalkMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Holding W moved the camera {Walk:F3} m horizontally; expected at least {MinimumWalkMeters} m."));
        }
        if(Rise<MinimumJumpMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The jump raised the camera {Rise:F3} m; expected at least {MinimumJumpMeters} m."));
        }
        if(Descent<MinimumDescentMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera came down {Descent:F3} m from the jump apex; expected at least {MinimumDescentMeters} m."));
        }
        if(Lowest<MinimumCameraHeight){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera dropped to {Lowest:F3} m, below the {MinimumCameraHeight} m floor bound."));
        }
        return Failures;
    }
}
