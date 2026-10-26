using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// 破壊の見本(ADR-0078/M0113、<c>samples/CSharpGameEngine.Samples.Destruction</c>)の実走検証。見本を自動実行の入力つきで WARP で動かし、
/// 見本が毎フレーム出す <c>CGE-DESTRUCTION-SAMPLE</c> の行(壊れたもの・切れた結合・ばらけた破片・眠った破片・一番低い破片・一番速い破片)で、
/// 始まってすぐ壁に当たる球と落ちる木箱が壊れること、歩いてぶつかった木箱が壊れること、終わりに 6 個すべてが壊れ、爆発(E)が破片を押すこと、破片が床を抜けずに積もって静まることを判定する。
/// M0124: 壁が 2 段のクラスター(<c>ClusterLevels</c> 2)で調理されていること、走行中にどこかの切れていない結合が以前の衝撃の内部ひずみを持ったことも判定する。
/// 続けて <c>Physics.Cli</c> で見本の <c>DestructionBake</c> シーン(床の上の木箱の中で爆発)を 2 回焼き、焼いたキャッシュを再生して書き出し、3 つが同じバイト列であること
/// (破片の物理と切れた結合が決定的で、再生が焼いたときと同じ世界を作ること)を判定する。
/// 固定 tick は 1 フレームに上限回数だけ進むので、判定は順序と範囲に限り、正確な位置は比べない。
/// </summary>
public static class DestructionSampleVerification {
    public const int Frames=170;
    public const int WalkStartFrame=10;
    public const int WalkEndFrame=40;
    public const int FireFrame=50;
    public const int ExplodeFrame=70;
    /// <summary>見本のレベルの壊れるもの: 球を当てる壁、落ちる木箱、床の木箱 3 つと、その上の 1 つ。</summary>
    public const int ExpectedDestructibles=6;
    /// <summary>歩き出す前(球と落下)に壊れているもの。</summary>
    public const int MinimumBrokenBeforeWalk=2;
    /// <summary>終わりに、ばらけて動く破片がこれだけはある(実走で 64)。</summary>
    public const int MinimumLoosePieces=30;
    /// <summary>終わりの破片の重心の高さの下限(m)。床の上面は 0 で、一番薄い破片でも積もった重心はその上にある(実走で最低 0.078)。下回れば床を抜けた。</summary>
    public const float MinimumPieceHeight=0f;
    /// <summary>
    /// 走行中のどのフレームでも、破片の重心がこれより下に行かない(m)。M0124: 内部ひずみの蓄積で歩く間に壊れる木箱が増え、押された破片(7 m/s)が 1 フレームだけ
    /// 床へ 1 mm 食い込んだ(重心 −0.001 m、次のフレームで 0.031 m。破片と静的な箱の間に連続衝突判定は無い)。床を抜けた破片は落ち続けるので、一時の食い込みと区別して
    /// 5 cm より下を床抜けとする。
    /// </summary>
    public const float MinimumTransientPieceHeight=-0.05f;
    /// <summary>
    /// 終わり(爆発から約 8 秒後)の一番速い破片の速さの上限(m/s)。実走で 0.014。投げ出された直後は数 m/s(爆発の直後で 1.6)。
    /// 同じ調理の木箱どうしの破片が同じ形を共有して自分自身と衝突判定されていた不具合(M0113 で修正)では、山の破片が 0.5 m/s で跳ね続けた。
    /// </summary>
    public const float MaximumFinalSpeed=0.2f;
    /// <summary>M0124: 見本の壊れるものの段の数の最大(壁が 2 段)。</summary>
    public const int MinimumClusterLevels=2;
    /// <summary>焼く tick 数(4 秒)。爆発の 0.1 秒と、破片が落ちて床に当たるまでを含む。</summary>
    public const int BakeTicks=240;
    public const string BakeScene="CgeDestructionSample.Scenes.DestructionBake";
    public const string Prefix="CGE-DESTRUCTION-SAMPLE ";
    public static string InputSchedule=>string.Create(CultureInfo.InvariantCulture,$"W@{WalkStartFrame}-{WalkEndFrame};F@{FireFrame}-{FireFrame+1};E@{ExplodeFrame}-{ExplodeFrame+1}");
    /// <summary>One <c>CGE-DESTRUCTION-SAMPLE</c> line.</summary>
    public readonly record struct SampleFrame(int Frame,long Tick,int Destructibles,int Broken,int Bonds,int Loose,int Sleeping,float Lowest,float Fastest,int Strained=0,int Levels=0);
    public static string ExecutablePath(string RepositoryRoot,string Configuration)=>Path.Combine(
        RepositoryRoot,"samples","CSharpGameEngine.Samples.Destruction","bin",Configuration,"net10.0-windows10.0.19041.0","win-x64","CgeDestructionSample.exe");
    public static string PhysicsCliPath(string RepositoryRoot,string Configuration)=>Path.Combine(
        RepositoryRoot,"tools","CSharpGameEngine.Physics.Cli","bin",Configuration,"net10.0-windows10.0.19041.0","win-x64","CSharpGameEngine.Physics.Cli.exe");
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Executable=ExecutablePath(RepositoryRoot,Configuration);
        if(!File.Exists(Executable)){
            await Error.WriteLineAsync($"The destruction sample is not built: {Executable}").ConfigureAwait(false);
            return 2;
        }
        var PhysicsCli=PhysicsCliPath(RepositoryRoot,Configuration);
        if(!File.Exists(PhysicsCli)){
            await Error.WriteLineAsync($"The physics CLI is not built: {PhysicsCli}").ConfigureAwait(false);
            return 2;
        }
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","destruction.sample",Adapter);
        Directory.CreateDirectory(Evidence);
        var Live=await RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false);
        var Bake=await RunBakeAsync(PhysicsCli,Path.ChangeExtension(Executable,".dll"),Evidence,Output,Error,CancellationToken).ConfigureAwait(false);
        return Live!=0?Live:Bake;
    }
    private static async Task<int>RunGameAsync(string Executable,string Evidence,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
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
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start the destruction sample.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Diagnostics=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        var Text=await Standard.ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(Evidence,"output.txt"),Text,CancellationToken).ConfigureAwait(false);
        await Error.WriteAsync(await Diagnostics.ConfigureAwait(false)).ConfigureAwait(false);
        if(Process.ExitCode!=0||!File.Exists(ReportPath)){
            await Error.WriteLineAsync($"The destruction sample exited with {Process.ExitCode} and report present={File.Exists(ReportPath)}.").ConfigureAwait(false);
            return 1;
        }
        using var Report=JsonDocument.Parse(await File.ReadAllBytesAsync(ReportPath,CancellationToken).ConfigureAwait(false));
        var Failures=Evaluate(Report.RootElement,Parse(Text),out var Summary);
        await Output.WriteLineAsync("CGE-DESTRUCTION-SAMPLE-VERIFIED "+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        return Failures.Count==0?0:1;
    }
    /// <summary>Bakes the sample's bake scene twice and plays the first bake back; the three caches must be the same bytes.</summary>
    private static async Task<int>RunBakeAsync(string PhysicsCli,string GameAssembly,string Evidence,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Ticks=BakeTicks.ToString(CultureInfo.InvariantCulture);
        var First=Path.Combine(Evidence,"bake-1.cgephyscache");
        var Second=Path.Combine(Evidence,"bake-2.cgephyscache");
        var Replayed=Path.Combine(Evidence,"replay.cgephyscache");
        foreach(var (Arguments,Name) in new (string[]Arguments,string Name)[]{
            (["bake","--assembly",GameAssembly,BakeScene,First,Ticks,"verify-destruction"],"first bake"),
            (["bake","--assembly",GameAssembly,BakeScene,Second,Ticks,"verify-destruction"],"second bake"),
            (["replay","--assembly",GameAssembly,BakeScene,First,Replayed,Ticks,"verify-destruction"],"replay"),
        }){
            var (ExitCode,Text)=await RunToolAsync(PhysicsCli,Arguments,CancellationToken).ConfigureAwait(false);
            if(ExitCode!=0){
                await Error.WriteLineAsync($"The {Name} of {BakeScene} exited with {ExitCode}: {Text.Trim()}").ConfigureAwait(false);
                return 1;
            }
        }
        var Failures=CompareCaches(await File.ReadAllBytesAsync(First,CancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(Second,CancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(Replayed,CancellationToken).ConfigureAwait(false),out var Summary);
        await Output.WriteLineAsync("CGE-DESTRUCTION-BAKE-VERIFIED "+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        return Failures.Count==0?0:1;
    }
    private static async Task<(int ExitCode,string Text)>RunToolAsync(string Tool,IEnumerable<string>Arguments,CancellationToken CancellationToken){
        var Start=new ProcessStartInfo(Tool) { UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true };
        foreach(var Argument in Arguments){ Start.ArgumentList.Add(Argument); }
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException($"Could not start {Tool}.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Diagnostics=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        return (Process.ExitCode,(await Standard.ConfigureAwait(false))+(await Diagnostics.ConfigureAwait(false)));
    }
    /// <summary>The three caches are the same bytes: the bake is deterministic and the playback reproduces it.</summary>
    public static IReadOnlyList<string>CompareCaches(byte[]First,byte[]Second,byte[]Replayed,out string Summary){
        List<string>Failures=[];
        var Deterministic=First.AsSpan().SequenceEqual(Second);
        var PlayedBack=First.AsSpan().SequenceEqual(Replayed);
        Summary=string.Create(CultureInfo.InvariantCulture,$"bytes={First.Length} deterministic={Deterministic} playbackMatches={PlayedBack}");
        if(First.Length==0){ Failures.Add("The bake wrote an empty physics cache."); }
        if(!Deterministic){ Failures.Add($"Two bakes of {BakeScene} wrote different physics caches ({First.Length} and {Second.Length} bytes)."); }
        if(!PlayedBack){ Failures.Add($"Playing the bake of {BakeScene} back wrote a different physics cache ({First.Length} and {Replayed.Length} bytes): the playback does not reproduce the bake."); }
        return Failures;
    }
    /// <summary>The <c>CGE-DESTRUCTION-SAMPLE</c> lines of the sample's output, in order; other lines are ignored.</summary>
    public static IReadOnlyList<SampleFrame>Parse(string Output){
        List<SampleFrame>Frames=[];
        foreach(var Line in Output.Split('\n')){
            var Text=Line.Trim();
            if(!Text.StartsWith(Prefix,StringComparison.Ordinal)){ continue; }
            Dictionary<string,string>Values=new(StringComparer.Ordinal);
            foreach(var Pair in Text[Prefix.Length..].Split(' ',StringSplitOptions.RemoveEmptyEntries)){
                var Equals=Pair.IndexOf('=',StringComparison.Ordinal);
                if(Equals>0){ Values[Pair[..Equals]]=Pair[(Equals+1)..]; }
            }
            if(TryInt(Values,"frame",out var Frame)&&long.TryParse(Values.GetValueOrDefault("tick"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var Tick)
                &&TryInt(Values,"destructibles",out var Destructibles)&&TryInt(Values,"broken",out var Broken)&&TryInt(Values,"bonds",out var Bonds)
                &&TryInt(Values,"loose",out var Loose)&&TryInt(Values,"sleeping",out var Sleeping)
                &&TryFloat(Values,"lowest",out var Lowest)&&TryFloat(Values,"fastest",out var Fastest)){
                // M0124: strained and levels came later; a line without them reads as 0.
                _=TryInt(Values,"strained",out var Strained);
                _=TryInt(Values,"levels",out var Levels);
                Frames.Add(new SampleFrame(Frame,Tick,Destructibles,Broken,Bonds,Loose,Sleeping,Lowest,Fastest,Strained,Levels));
            }
        }
        return Frames;
    }
    private static bool TryInt(Dictionary<string,string>Values,string Key,out int Value)=>
        int.TryParse(Values.GetValueOrDefault(Key),NumberStyles.Integer,CultureInfo.InvariantCulture,out Value);
    private static bool TryFloat(Dictionary<string,string>Values,string Key,out float Value)=>
        float.TryParse(Values.GetValueOrDefault(Key),NumberStyles.Float,CultureInfo.InvariantCulture,out Value);
    /// <summary>レポートと見本の行を判定し、失敗理由を返す(空なら合格)。<paramref name="Summary"/>は観測値の要約。</summary>
    public static IReadOnlyList<string>Evaluate(JsonElement Report,IReadOnlyList<SampleFrame>Lines,out string Summary){
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
        if(Lines.Count<Frames||Enumerable.Range(1,Frames).Any(Frame=>!Lines.Any(Line=>Line.Frame==Frame))){
            Failures.Add($"Expected a {Prefix.Trim()} line for each of {Frames} frames, found {Lines.Count}.");
            return Failures;
        }
        SampleFrame At(int Frame)=>Lines.Last(Line=>Line.Frame==Frame);
        var BeforeWalk=At(WalkStartFrame-1);
        var BeforeFire=At(FireFrame-1);
        var BeforeExplosion=At(ExplodeFrame-1);
        var Final=At(Frames);
        var Loaded=Lines.Min(static Line=>Line.Destructibles);
        var Lowest=Lines.Where(static Line=>Line.Loose>0).Select(static Line=>Line.Lowest).DefaultIfEmpty(float.NaN).Min();
        var MostStrained=Lines.Max(static Line=>Line.Strained);
        var AfterExplosion=At(ExplodeFrame+3);
        Summary=string.Create(CultureInfo.InvariantCulture,
            $"destructibles={Final.Destructibles} broken={BeforeWalk.Broken}/{BeforeFire.Broken}/{BeforeExplosion.Broken}/{Final.Broken} bonds={Final.Bonds} loose={Final.Loose} sleeping={Final.Sleeping} lowest={Lowest:F3}/{Final.Lowest:F3} fastest={Final.Fastest:F3} pushed={BeforeExplosion.Fastest:F3}->{AfterExplosion.Fastest:F3} levels={Final.Levels} mostStrained={MostStrained} d3d12Errors={Errors}");
        if(Loaded!=ExpectedDestructibles||Final.Destructibles!=ExpectedDestructibles){
            Failures.Add($"The sample loaded {Loaded} to {Final.Destructibles} destructible(s); expected {ExpectedDestructibles} (a missing or rejected fracture asset is not counted).");
        }
        if(BeforeWalk.Broken<MinimumBrokenBeforeWalk){
            Failures.Add($"{BeforeWalk.Broken} destructible(s) broke before the character walked; expected at least {MinimumBrokenBeforeWalk} (the wall the ball hits and the crate that falls).");
        }
        if(BeforeFire.Broken<=BeforeWalk.Broken){
            Failures.Add($"Walking into the crates broke nothing ({BeforeWalk.Broken} broken before, {BeforeFire.Broken} after).");
        }
        // M0124: with the internal strain the walk may break every crate before the explosion; then the explosion only pushes the pieces.
        if(Final.Broken!=Final.Destructibles){
            Failures.Add($"The explosion left {Final.Destructibles-Final.Broken} destructible(s) whole ({BeforeExplosion.Broken} broken before it, {Final.Broken} after); expected every one broken at the end.");
        }
        if(Final.Broken==BeforeExplosion.Broken&&!(AfterExplosion.Fastest>BeforeExplosion.Fastest)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The explosion neither broke anything nor pushed the pieces (fastest {BeforeExplosion.Fastest:F3} m/s before it, {AfterExplosion.Fastest:F3} m/s after)."));
        }
        if(Final.Loose<MinimumLoosePieces){
            Failures.Add($"{Final.Loose} loose piece(s) at the end; expected at least {MinimumLoosePieces}.");
        }
        if(!(Lowest>=MinimumTransientPieceHeight)||!(Final.Lowest>=MinimumPieceHeight)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"A loose piece's centre went down to {Lowest:F3} m ({Final.Lowest:F3} m at the end); the floor's top is at {MinimumPieceHeight} m (a piece fell through it)."));
        }
        if(!(Final.Fastest<=MaximumFinalSpeed)){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The fastest loose piece still moves at {Final.Fastest:F3} m/s at the end; expected the pieces to settle (at most {MaximumFinalSpeed} m/s)."));
        }
        if(Final.Levels<MinimumClusterLevels){
            Failures.Add($"The sample's destructibles have at most {Final.Levels} cluster level(s); expected the wall's {MinimumClusterLevels} (a stale cook or a lost ClusterLevels).");
        }
        if(MostStrained==0){
            Failures.Add("No intact bond ever carried internal strain from an earlier hit; expected the hits that did not break a bond to leave it strained.");
        }
        return Failures;
    }
}
